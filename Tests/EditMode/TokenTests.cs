using System;
using System.Text;
using NUnit.Framework;

namespace LiveKit.EditModeTests
{
    public class TokenTests
    {
        private const string TestApiKey = "test-api-key";
        private const string TestApiSecret = "test-api-secret";

        private LiveKitToken.Claims ValidClaims => new LiveKitToken.Claims
        {
            iss = TestApiKey,
            sub = "test-identity",
            video = new LiveKitToken.VideoGrants
            {
                room = "test-room",
                roomJoin = true,
                canPublish = true,
                canSubscribe = true,
            }
        };

        [Test]
        public void Encode_WithValidClaims_ReturnsThreeSegmentJwt()
        {
            var token = LiveKitToken.Encode(ValidClaims, TestApiSecret);

            Assert.IsNotNull(token);
            var segments = token.Split('.');
            Assert.AreEqual(3, segments.Length, "JWT should have header.payload.signature");
        }

        [Test]
        public void Encode_WithoutIss_ThrowsArgumentException()
        {
            var claims = ValidClaims;
            claims.iss = null;

            Assert.Throws<ArgumentException>(() => LiveKitToken.Encode(claims, TestApiSecret));
        }

        [Test]
        public void Encode_WithEmptyIss_ThrowsArgumentException()
        {
            var claims = ValidClaims;
            claims.iss = "";

            Assert.Throws<ArgumentException>(() => LiveKitToken.Encode(claims, TestApiSecret));
        }

        [Test]
        public void Encode_RoomJoinWithoutIdentity_ThrowsArgumentException()
        {
            var claims = ValidClaims;
            claims.sub = null;

            Assert.Throws<ArgumentException>(() => LiveKitToken.Encode(claims, TestApiSecret));
        }

        [Test]
        public void Encode_RoomJoinWithoutRoom_ThrowsArgumentException()
        {
            var claims = ValidClaims;
            claims.video = new LiveKitToken.VideoGrants { roomJoin = true, room = null };

            Assert.Throws<ArgumentException>(() => LiveKitToken.Encode(claims, TestApiSecret));
        }

        [Test]
        public void Encode_SetsDefaultNbfAndExp_WhenZero()
        {
            var claims = ValidClaims;
            claims.nbf = 0;
            claims.exp = 0;

            var token = LiveKitToken.Encode(claims, TestApiSecret);
            var payload = DecodePayload(token);

            // Should contain non-zero nbf and exp
            StringAssert.Contains("\"nbf\":", payload);
            StringAssert.Contains("\"exp\":", payload);
            // The values should not be 0
            StringAssert.DoesNotContain("\"nbf\":0", payload);
            StringAssert.DoesNotContain("\"exp\":0", payload);
        }

        [Test]
        public void Encode_PreservesExplicitNbfAndExp()
        {
            var claims = ValidClaims;
            claims.nbf = 1000000;
            claims.exp = 2000000;

            var token = LiveKitToken.Encode(claims, TestApiSecret);
            var payload = DecodePayload(token);

            StringAssert.Contains("1000000", payload);
            StringAssert.Contains("2000000", payload);
        }

        [Test]
        public void Encode_PayloadContainsExpectedClaims()
        {
            var token = LiveKitToken.Encode(ValidClaims, TestApiSecret);
            var payload = DecodePayload(token);

            StringAssert.Contains(TestApiKey, payload);
            StringAssert.Contains("test-identity", payload);
            StringAssert.Contains("test-room", payload);
        }

        [Test]
        public void Encode_NonJoinToken_DoesNotRequireIdentityOrRoom()
        {
            var claims = new LiveKitToken.Claims
            {
                iss = TestApiKey,
                video = new LiveKitToken.VideoGrants { roomCreate = true }
            };

            Assert.DoesNotThrow(() => LiveKitToken.Encode(claims, TestApiSecret));
        }

        private static string DecodePayload(string jwt)
        {
            var segments = jwt.Split('.');
            // Re-pad the base64url string
            var base64 = segments[1].Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            var bytes = Convert.FromBase64String(base64);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
