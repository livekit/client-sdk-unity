using LiveKit.Internal.FFI;
using LiveKit.Internal.FFI.Requests;
using LiveKit.Proto;
using UnityEngine;

public class TokenSourceFfi
{
    internal readonly FfiHandle Handle;

    public TokenSourceFfi()
    {
        using var request = FFIBridge.Instance.NewRequest<NewTokenSourceRequest>();
        using var response = request.Send();
        FfiResponse res = response;

        var tokenSource = res.NewTokenSource.TokenSource;
        Handle = FfiHandle.FromOwnedHandle(tokenSource.Handle);

        Debug.Log($"The token source handle is {Handle.DangerousGetHandle().ToInt64()}");
    }
}