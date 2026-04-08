using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Reflection;
using LiveKit.Proto;

namespace LiveKit.Internal.FFIClients
{
    public static class FfiRequestExtensions
    {
        private static long nextRequestAsyncId;
        private static readonly ConcurrentDictionary<Type, Action<object, ulong>?> requestAsyncIdSetters = new();
        private static readonly ConcurrentDictionary<Type, Action<FfiRequest, object>> injectors = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong InitializeRequestAsyncId<T>(this T request)
        {
            // The async request path needs a client-generated request ID before the request
            // crosses the FFI boundary. That allows Unity to register a completion handler first,
            // then send the request, which removes the race where Rust could emit the callback
            // before Unity had subscribed.
            //
            // Historically this method used a large type switch and set RequestAsyncId on each
            // generated protobuf request class explicitly. That is fast, but it tightly couples
            // the code to the exact list of generated request types.
            //
            // This implementation is intentionally based on normal .NET reflection instead of
            // protobuf reflection APIs. The only contract it relies on is:
            //   1. the generated request type has a public instance property named RequestAsyncId
            //   2. that property is writable
            //   3. that property has type ulong
            //
            // Because of that, the code will keep working even if the protobuf runtime changes
            // (for example, switching to a lighter runtime such as "protolite"), as long as the
            // generated C# surface still exposes the same property shape.
            //
            // The expensive part here is reflection lookup, not assigning one ulong. We therefore
            // cache a setter delegate per concrete request type. The first request of a given type
            // pays the reflection cost; subsequent requests reuse the cached delegate.
            //
            // The cached delegate below still calls PropertyInfo.SetValue internally. That is not
            // as fast as a fully compiled expression setter, but it is simpler and safer for Unity
            // runtimes, especially IL2CPP / AOT targets where expression compilation can be less
            // predictable. In practice, this cost is tiny compared with protobuf serialization,
            // the native FFI call, and response parsing.
            if (request == null)
            {
                return 0;
            }

            var setter = requestAsyncIdSetters.GetOrAdd(request.GetType(), static type =>
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
                var property = type.GetProperty("RequestAsyncId", flags);

                if (property == null || !property.CanWrite || property.PropertyType != typeof(ulong))
                {
                    return null;
                }

                return (target, value) => property.SetValue(target, value);
            });

            if (setter == null)
            {
                return 0;
            }

            var requestAsyncId = (ulong)Interlocked.Increment(ref nextRequestAsyncId);
            setter(request, requestAsyncId);
            return requestAsyncId;
        }

        /// <summary>
        /// Sets the appropriate oneof field on <paramref name="ffiRequest"/> to <paramref name="request"/>.
        /// </summary>
        /// <remarks>
        /// Uses the same reflection + caching approach as <see cref="InitializeRequestAsyncId{T}"/>.
        /// Each concrete request type is matched to the FfiRequest property whose type equals it.
        /// Protobuf oneof fields generate exactly one property per variant type, so the match is
        /// unambiguous. The first call for a given type pays the reflection cost; subsequent calls
        /// reuse the cached delegate.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Inject<T>(this FfiRequest ffiRequest, T request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var injector = injectors.GetOrAdd(request.GetType(), static type =>
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
                var property = typeof(FfiRequest).GetProperties(flags)
                    .FirstOrDefault(p => p.PropertyType == type && p.CanWrite);

                if (property == null)
                    throw new InvalidOperationException(
                        $"No FfiRequest property found for type {type.FullName}");

                return (req, val) => property.SetValue(req, val);
            });

            injector(ffiRequest, request);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnsureClean(this FfiResponse response)
        {
            if (response.MessageCase != FfiResponse.MessageOneofCase.None)
                throw new InvalidOperationException("Response is not cleared");
        }
    }
}
