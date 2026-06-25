using System.Collections.Generic;
using LiveKit.Internal;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Proto;

namespace LiveKit
{
    public enum EncryptionType
    {
        NONE = 0,
        GCM = 1,
        CUSTOM = 2
    }

    public class KeyProviderOptions
    {
        public byte[] SharedKey;
        public int RatchetWindowSize;
        public byte[] RatchetSalt;
        public int FailureTolerance;

    }

    public class E2EEOptions
    {
        public KeyProviderOptions KeyProviderOptions;
        public EncryptionType EncryptionType;

        public Proto.E2eeOptions ToProto()
        {
            var proto = new Proto.E2eeOptions();

            return proto;
        }
    }

    public class KeyProvider
    {
        internal FfiHandle RoomHandle;
        public KeyProviderOptions KeyProviderOptions;

        public KeyProvider(FfiHandle roomHandle, KeyProviderOptions keyProviderOptions)
        {
            RoomHandle = roomHandle;
            KeyProviderOptions = keyProviderOptions;
        }

        public void SetSharedKey(byte[] key, int keyIndex)
        {
            using var request = FFIBridge.Instance.NewRequest<SetSharedKeyRequest>();
            var e2ee = request.request;
            e2ee.KeyIndex = keyIndex;
            e2ee.SharedKey = Google.Protobuf.ByteString.CopyFrom(key);

            using var response = request.Send();
        }

        public byte[] GetSharedKey(int keyIndex)
        {
            using var request = FFIBridge.Instance.NewRequest<GetSharedKeyRequest>();
            var e2ee = request.request;
            e2ee.KeyIndex = keyIndex;

            using var response = request.Send();
            FfiResponse resp = response;
            return resp.E2Ee.GetSharedKey.Key.ToByteArray();
        }

        public byte[] RatchetSharedKey(int keyIndex)
        {
            using var request = FFIBridge.Instance.NewRequest<RatchetSharedKeyRequest>();
            var e2ee = request.request;
            e2ee.KeyIndex = keyIndex;

            using var response = request.Send();
            FfiResponse resp = response;
            return resp.E2Ee.RatchetSharedKey.NewKey.ToByteArray();
        }

        public void SetKey(string participantIdentity, byte[] key, int keyIndex)
        {
            using var request = FFIBridge.Instance.NewRequest<SetKeyRequest>();
            var e2ee = request.request;
            e2ee.KeyIndex = keyIndex;
            e2ee.ParticipantIdentity = participantIdentity;

            using var response = request.Send();
        }

        public byte[] GetKey(string participantIdentity, int keyIndex)
        {
            using var request = FFIBridge.Instance.NewRequest<GetKeyRequest>();
            var e2ee = request.request;
            e2ee.KeyIndex = keyIndex;
            e2ee.ParticipantIdentity = participantIdentity;

            using var response = request.Send();
            FfiResponse resp = response;
            return resp.E2Ee.GetKey.Key.ToByteArray();
        }

        public byte[] RatchetKey(string participantIdentity, int keyIndex)
        {
            using var request = FFIBridge.Instance.NewRequest<RatchetKeyRequest>();
            var e2ee = request.request;
            e2ee.KeyIndex = keyIndex;
            e2ee.ParticipantIdentity = participantIdentity;

            using var response = request.Send();
            FfiResponse resp = response;
            return resp.E2Ee.RatchetKey.NewKey.ToByteArray();
        }
    }


    public class FrameCryptor
    {
        internal FfiHandle RoomHandle;
        public string ParticipantIdentity;
        public string TrackSid;
        public bool Enabled;
        public int KeyIndex;

        public FrameCryptor(FfiHandle roomHandle, string identity, string trackSid, bool enabled, int keyIndex)
        {
            RoomHandle = roomHandle;
            ParticipantIdentity = identity;
            TrackSid = trackSid;
            Enabled = enabled;
            KeyIndex = keyIndex;
        }

        public void SetKeyIndex(int keyIndex)
        {
            KeyIndex = keyIndex;
            using var request = FFIBridge.Instance.NewRequest<FrameCryptorSetKeyIndexRequest>();
            var e2ee = request.request;
            e2ee.KeyIndex = keyIndex;
            e2ee.ParticipantIdentity = ParticipantIdentity;
            request.Send();
        }

        public void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            using var request = FFIBridge.Instance.NewRequest<FrameCryptorSetEnabledRequest>();
            var e2ee = request.request;
            e2ee.Enabled = enabled;
            e2ee.ParticipantIdentity = ParticipantIdentity;
            request.Send();
        }
    }


    public class E2EEManager
    {
        internal FfiHandle RoomHandle;
        public KeyProvider KeyProvider;
        public E2EEOptions E2EEOptions;

        public E2EEManager(FfiHandle roomHandle, E2EEOptions e2EEOptions)
        {
            RoomHandle = roomHandle;
            KeyProvider = new KeyProvider(roomHandle, e2EEOptions.KeyProviderOptions);
            E2EEOptions = e2EEOptions;
        }

        public void setEnabled(bool enabled)
        {
            using var request = FFIBridge.Instance.NewRequest<E2eeManagerSetEnabledRequest>();
            var e2ee = request.request;
            e2ee.Enabled = enabled;
            request.Send();
        }

        public List<FrameCryptor> frameCryptors()
        {
            using var request = FFIBridge.Instance.NewRequest<E2eeManagerSetEnabledRequest>();
            var e2ee = request.request;
   
            using var response = request.Send();
            FfiResponse resp = response;

            List<FrameCryptor> cryptors = new List<FrameCryptor>();

            foreach(var c in resp.E2Ee.ManagerGetFrameCryptors.FrameCryptors)
            {
                cryptors.Add(new FrameCryptor(RoomHandle, c.ParticipantIdentity, c.TrackSid, c.Enabled, c.KeyIndex));
            }
            return cryptors;
        }
    }
}