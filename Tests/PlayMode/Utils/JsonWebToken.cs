/// This is a simplified version of JWT-for-Unity,
/// released into the public domain:
/// https://github.com/monry/JWT-for-Unity/

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace LiveKit.PlayModeTests.Utils
{
    public static class JsonWebToken
    {
        public enum Algorithm
        {
            HS256,
            HS384,
            HS512
        }

        private static readonly IDictionary<Algorithm, Func<byte[], byte[], byte[]>> HashAlgorithms;

        static JsonWebToken()
        {
            HashAlgorithms = new Dictionary<Algorithm, Func<byte[], byte[], byte[]>>
            {
                { Algorithm.HS256, ComputeHash<HMACSHA256> },
                { Algorithm.HS384, ComputeHash<HMACSHA384> },
                { Algorithm.HS512, ComputeHash<HMACSHA512> }
            };
        }

        private static byte[] ComputeHash<T>(byte[] key, byte[] value) where T : HMAC, new()
        {
            using (var sha = (T)Activator.CreateInstance(typeof(T), key))
                return sha.ComputeHash(value);
        }

        [Serializable]
        private struct Header
        {
            public string typ;
            public string alg;
        }

        public static string Encode(object payload, string key, Algorithm algorithm)
        {
            var segments = new List<string>();
            var header = new Header { typ = "JWT", alg = algorithm.ToString() };

            byte[] headerBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(header));
            byte[] payloadBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));

            segments.Add(Base64UrlEncode(headerBytes));
            segments.Add(Base64UrlEncode(payloadBytes));

            var stringToSign = string.Join(".", segments.ToArray());
            var bytesToSign = Encoding.UTF8.GetBytes(stringToSign);

            byte[] signature = HashAlgorithms[algorithm](Encoding.UTF8.GetBytes(key), bytesToSign);
            segments.Add(Base64UrlEncode(signature));

            return string.Join(".", segments.ToArray());
        }

        // from JWT spec
        private static string Base64UrlEncode(byte[] input)
        {
            var output = Convert.ToBase64String(input);
            output = output.Split('=')[0]; // Remove any trailing '='s
            output = output.Replace('+', '-'); // 62nd char of encoding
            output = output.Replace('/', '_'); // 63rd char of encoding
            return output;
        }
    }
}