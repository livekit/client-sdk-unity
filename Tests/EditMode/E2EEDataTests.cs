using NUnit.Framework;

namespace LiveKit.EditModeTests
{
    /// <summary>
    /// EditMode coverage for the public E2EE data-holder surface. Functional coverage
    /// (KeyProvider round-trips, E2EE-enabled room media flow) is deferred: the
    /// KeyProvider FFI methods currently use the wrong request type (SetSharedKeyRequest
    /// directly rather than a wrapping E2eeRequest), so every call throws
    /// "Unknown request type" at dispatch. See Logs~/More Claude Tests 3/e2ee-findings.html
    /// for details.
    /// </summary>
    public class E2EEDataTests
    {
        [Test]
        public void EncryptionType_Values_MatchDocumentedConstants()
        {
            Assert.AreEqual(0, (int)EncryptionType.NONE);
            Assert.AreEqual(1, (int)EncryptionType.GCM);
            Assert.AreEqual(2, (int)EncryptionType.CUSTOM);
        }

        [Test]
        public void KeyProviderOptions_DefaultInstance_HasNullKeysAndZeroCounters()
        {
            var options = new KeyProviderOptions();
            Assert.IsNull(options.SharedKey);
            Assert.IsNull(options.RatchetSalt);
            Assert.AreEqual(0, options.RatchetWindowSize);
            Assert.AreEqual(0, options.FailureTolerance);
        }

        [Test]
        public void KeyProviderOptions_AllowsPublicFieldAssignment()
        {
            var key = new byte[] { 0x01, 0x02 };
            var salt = new byte[] { 0xAA, 0xBB };
            var options = new KeyProviderOptions
            {
                SharedKey = key,
                RatchetSalt = salt,
                RatchetWindowSize = 16,
                FailureTolerance = 3
            };

            Assert.AreSame(key, options.SharedKey);
            Assert.AreSame(salt, options.RatchetSalt);
            Assert.AreEqual(16, options.RatchetWindowSize);
            Assert.AreEqual(3, options.FailureTolerance);
        }

        [Test]
        public void E2EEOptions_DefaultInstance_HasNullKeyProviderAndNoneEncryption()
        {
            var options = new E2EEOptions();
            Assert.IsNull(options.KeyProviderOptions);
            Assert.AreEqual(EncryptionType.NONE, options.EncryptionType);
        }

        [Test]
        public void E2EEOptions_ToProto_DoesNotThrow()
        {
            var options = new E2EEOptions
            {
                EncryptionType = EncryptionType.GCM,
                KeyProviderOptions = new KeyProviderOptions
                {
                    SharedKey = new byte[] { 0x01 },
                    RatchetWindowSize = 16
                }
            };

            // E2EEOptions.ToProto currently returns an empty proto (fields are not copied).
            // This is tracked as a functional bug; this test just pins that ToProto is
            // at least invokable so future fixes don't introduce a regression in throw
            // behavior.
            Assert.DoesNotThrow(() => options.ToProto());
        }
    }
}
