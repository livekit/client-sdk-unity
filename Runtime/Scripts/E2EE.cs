using System.Collections.Generic;
using LiveKit.Internal;
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
        internal ulong RoomHandle;
        public KeyProviderOptions KeyProviderOptions;

        public KeyProvider(ulong roomHandle, KeyProviderOptions keyProviderOptions)
        {
            RoomHandle = roomHandle;
            KeyProviderOptions = keyProviderOptions;
        }

        public void SetSharedKey(byte[] key, int keyIndex)
        {
            var e2eeReq = new E2eeRequest();
            var req = new FfiRequest();
            req.E2Ee = e2eeReq;
            req.E2Ee.RoomHandle = RoomHandle;
            req.E2Ee.SetSharedKey = new SetSharedKeyRequest();
            req.E2Ee.SetSharedKey.KeyIndex = keyIndex;
            req.E2Ee.SetSharedKey.SharedKey = Google.Protobuf.ByteString.CopyFrom(key);

            FfiClient.SendRequest(req);
        }

        public byte[] GetSharedKey(int keyIndex)
        {
            var e2eeReq = new E2eeRequest();
            var req = new FfiRequest();
            req.E2Ee = e2eeReq;
            req.E2Ee.RoomHandle = RoomHandle;
            req.E2Ee.GetSharedKey = new GetSharedKeyRequest();
            req.E2Ee.GetSharedKey.KeyIndex = keyIndex;
            var resp = FfiClient.SendRequest(req);

            return resp.E2Ee.GetSharedKey.Key.ToByteArray();
        }

        public byte[] RatchetSharedKey(int keyIndex)
        {
            var e2eeReq = new E2eeRequest();
            var req = new FfiRequest();
            req.E2Ee = e2eeReq;
            req.E2Ee.RoomHandle = RoomHandle;
            req.E2Ee.RatchetSharedKey = new RatchetSharedKeyRequest();
            req.E2Ee.RatchetSharedKey.KeyIndex = keyIndex;
            var resp = FfiClient.SendRequest(req);

            return resp.E2Ee.RatchetSharedKey.NewKey.ToByteArray();
        }

        public void SetKey(string participantIdentity, byte[] key, int keyIndex)
        {
            var e2eeReq = new E2eeRequest();
            var req = new FfiRequest();
            req.E2Ee = e2eeReq;
            req.E2Ee.RoomHandle = RoomHandle;
            req.E2Ee.SetKey = new SetKeyRequest();
            req.E2Ee.SetKey.KeyIndex = keyIndex;
            req.E2Ee.SetKey.ParticipantIdentity = participantIdentity;
            req.E2Ee.SetKey.Key = Google.Protobuf.ByteString.CopyFrom(key);

            FfiClient.SendRequest(req);
        }

        public byte[] GetKey(string participantIdentity, int keyIndex)
        {
            var e2eeReq = new E2eeRequest();
            var req = new FfiRequest();
            req.E2Ee = e2eeReq;
            req.E2Ee.RoomHandle = RoomHandle;
            req.E2Ee.GetKey = new GetKeyRequest();
            req.E2Ee.GetKey.KeyIndex = keyIndex;
            req.E2Ee.GetKey.ParticipantIdentity = participantIdentity;
            var resp = FfiClient.SendRequest(req);

            return resp.E2Ee.GetKey.Key.ToByteArray();
        }

        public byte[] RatchetKey(string participantIdentity, int keyIndex)
        {
            var e2eeReq = new E2eeRequest();
            var req = new FfiRequest();
            req.E2Ee = e2eeReq;
            req.E2Ee.RoomHandle = RoomHandle;
            req.E2Ee.RatchetKey = new RatchetKeyRequest();
            req.E2Ee.RatchetKey.KeyIndex = keyIndex;
            req.E2Ee.RatchetKey.ParticipantIdentity = participantIdentity;
            var resp = FfiClient.SendRequest(req);

            return resp.E2Ee.RatchetKey.NewKey.ToByteArray();
        }
    }


    public class FrameCryptor
    {
        internal ulong RoomHandle;
        public string ParticipantIdentity;
        public string TrackSid;
        public bool Enabled;
        public int KeyIndex;

        public FrameCryptor(ulong roomHandle, string identity, string trackSid, bool enabled, int keyIndex)
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
            var e2eeReq = new E2eeRequest();
            var req = new FfiRequest();
            req.E2Ee = e2eeReq;
            req.E2Ee.RoomHandle = RoomHandle;
            req.E2Ee.CryptorSetKeyIndex = new FrameCryptorSetKeyIndexRequest();
            req.E2Ee.CryptorSetKeyIndex.KeyIndex = keyIndex;
            req.E2Ee.CryptorSetKeyIndex.ParticipantIdentity = ParticipantIdentity;

            var resp = FfiClient.SendRequest(req);
        }

        public void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            var e2eeReq = new E2eeRequest();
            var req = new FfiRequest();
            req.E2Ee = e2eeReq;
            req.E2Ee.RoomHandle = RoomHandle;
            req.E2Ee.CryptorSetEnabled = new FrameCryptorSetEnabledRequest();
            req.E2Ee.CryptorSetEnabled.Enabled = enabled;
            req.E2Ee.CryptorSetEnabled.ParticipantIdentity = ParticipantIdentity;

            var resp = FfiClient.SendRequest(req);
        }
    }


    public class E2EEManager
    {
        internal ulong RoomHandle;
        public KeyProvider KeyProvider;
        public E2EEOptions E2EEOptions;

        public E2EEManager(ulong roomHandle, E2EEOptions e2EEOptions)
        {
            RoomHandle = roomHandle;
            KeyProvider = new KeyProvider(roomHandle, e2EEOptions.KeyProviderOptions);
            E2EEOptions = e2EEOptions;
        }

        public void setEnabled(bool enabled)
        {
            var e2eeReq = new E2eeRequest();
            var req = new FfiRequest();
            req.E2Ee = e2eeReq;
            req.E2Ee.RoomHandle = RoomHandle;
            req.E2Ee.ManagerSetEnabled = new E2eeManagerSetEnabledRequest();
            req.E2Ee.ManagerSetEnabled.Enabled = enabled;

            var resp = FfiClient.SendRequest(req);
        }

        public List<FrameCryptor> frameCryptors()
        {
            var e2eeReq = new E2eeRequest();
            var req = new FfiRequest();
            req.E2Ee = e2eeReq;
            req.E2Ee.RoomHandle = RoomHandle;
            req.E2Ee.ManagerGetFrameCryptors = new E2eeManagerGetFrameCryptorsRequest();

            var resp = FfiClient.SendRequest(req);
            List<FrameCryptor> cryptors = new List<FrameCryptor>();

            foreach(var c in resp.E2Ee.ManagerGetFrameCryptors.FrameCryptors)
            {
                cryptors.Add(new FrameCryptor(RoomHandle, c.ParticipantIdentity, c.TrackSid, c.Enabled, c.KeyIndex));
            }
            return cryptors;
        }
    }
}