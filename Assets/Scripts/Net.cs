using System;
using UnityEngine;

// ============================================================================
//  MULTIPLAYER (live versus race) — Photon PUN 2 wrapper.
//
//  All Photon-dependent code is behind #if PHOTON_UNITY_NETWORKING, a symbol
//  PUN 2 defines automatically when imported. So this file compiles BOTH before
//  and after you import the package: before, `Net` is an inert stub (Available
//  == false) and the VERSUS button shows a "needs Photon" note; after, it lights
//  up with no other changes.
//
//  Design: every device simulates only its OWN vampire and broadcasts its
//  position ~15x/sec via Photon RaiseEvent (no prefabs needed — this game is
//  built entirely from code). Everyone else is drawn as a translucent ghost.
//  The room CODE seeds the level, so all players race the identical track.
// ============================================================================

namespace TrustIssues
{
    /// <summary>
    /// A remote player's translucent vampire. Lerps smoothly toward the last
    /// position we received so 15 Hz network updates look continuous. Photon-
    /// free on purpose, so GameRoot can build/own these regardless of the netcode.
    /// </summary>
    public class Ghost : MonoBehaviour
    {
        public Vector3 target;
        Transform _vis;
        float _faceSign = 1f, _baseScale = 1f;

        public void Bind(Transform visual, float baseScale)
        { _vis = visual; _baseScale = baseScale; target = transform.position; }

        public void SetTarget(Vector3 pos, bool faceLeft)
        {
            // Face the direction of travel; fall back to the reported facing.
            if (Mathf.Abs(pos.x - target.x) > 0.02f) _faceSign = pos.x > target.x ? 1f : -1f;
            else _faceSign = faceLeft ? -1f : 1f;
            target = pos;
        }

        void Update()
        {
            transform.position = Vector3.Lerp(transform.position, target, 14f * Time.deltaTime);
            if (_vis != null)
                _vis.localScale = new Vector3(_faceSign * _baseScale, _baseScale, 1f);
        }
    }
}

#if PHOTON_UNITY_NETWORKING
namespace TrustIssues
{
    using Photon.Pun;
    using Photon.Realtime;
    using ExitGames.Client.Photon;

    /// <summary>
    /// Static facade the rest of the game talks to. Hides Photon entirely behind
    /// a handful of methods + callbacks so GameRoot has no Photon references.
    /// </summary>
    public static class Net
    {
        // Paste your Photon App ID here (the one from your PUN app dashboard).
        public const string AppId = "14e6b323-104c-45ba-95e3-0e023ee33dcf";

        public const byte EvState = 1;   // x, y, faceLeft
        public const byte EvWin   = 2;   // sender won the race

        public static bool Available => true;
        public static bool InRoom => PhotonNetwork.InRoom;
        public static string RoomCode => InRoom ? PhotonNetwork.CurrentRoom.Name : "";
        public static int LocalActor => PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : 0;
        public static int PlayerCount => InRoom ? PhotonNetwork.CurrentRoom.PlayerCount : 0;
        public static int Seed { get; private set; }

        // GameRoot subscribes to these.
        public static Action<int, Vector3, bool> OnState;  // actor, pos, faceLeft
        public static Action<int> OnLeft;                  // actor left
        public static Action<int> OnWin;                   // actor won the race
        public static Action OnRosterChanged;              // someone joined/left

        static NetClient _client;
        static NetClient Client
        {
            get
            {
                if (_client == null)
                {
                    var go = new GameObject("PhotonNet");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    _client = go.AddComponent<NetClient>();
                }
                return _client;
            }
        }

        public static void Host(Action onJoined, Action<string> onError)
            => Client.Connect(RandomCode(), onJoined, onError);

        public static void Join(string code, Action onJoined, Action<string> onError)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            if (code.Length < 4) { onError?.Invoke("Enter a 4-letter code."); return; }
            Client.Connect(code, onJoined, onError);
        }

        public static void Leave()
        {
            if (_client != null) _client.LeaveAll();
        }

        public static void SendState(Vector3 pos, bool faceLeft)
        {
            if (!PhotonNetwork.InRoom) return;
            object[] data = { pos.x, pos.y, faceLeft };
            PhotonNetwork.RaiseEvent(EvState, data,
                new RaiseEventOptions { Receivers = ReceiverGroup.Others },
                SendOptions.SendUnreliable);
        }

        public static void SendWin()
        {
            if (!PhotonNetwork.InRoom) return;
            PhotonNetwork.RaiseEvent(EvWin, null,
                new RaiseEventOptions { Receivers = ReceiverGroup.Others },
                SendOptions.SendReliable);
        }

        // A short, readable, unambiguous room code (no O/0, I/1).
        static string RandomCode()
        {
            const string abc = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var r = new System.Random();
            var c = new char[4];
            for (int i = 0; i < 4; i++) c[i] = abc[r.Next(abc.Length)];
            return new string(c);
        }

        // A platform-stable hash so every client derives the SAME level seed from
        // the room code (string.GetHashCode is randomized per-process — unusable).
        internal static void SetSeedFromCode(string code)
        {
            unchecked
            {
                int h = 23;
                foreach (char ch in code.ToUpperInvariant()) h = h * 31 + ch;
                Seed = h & 0x7fffffff;
            }
        }
    }

    /// <summary>The actual Photon client: connection lifecycle + event routing.</summary>
    public class NetClient : MonoBehaviourPunCallbacks, IOnEventCallback
    {
        string _code;
        Action _onJoined;
        Action<string> _onError;
        bool _connecting;

        public void Connect(string code, Action onJoined, Action<string> onError)
        {
            _code = code; _onJoined = onJoined; _onError = onError;

            if (PhotonNetwork.InRoom) { JoinNamed(); return; }
            if (_connecting) return;
            _connecting = true;

            // Stay connected even when this window/tab loses focus — without this,
            // a second (unfocused) instance pauses and gets a TimeoutDisconnect,
            // so the other player never sees its ghost.
            Application.runInBackground = true;
            PhotonNetwork.KeepAliveInBackground = 60000f; // ms of background grace

            var settings = PhotonNetwork.PhotonServerSettings.AppSettings;
            settings.AppIdRealtime = Net.AppId;
            settings.FixedRegion = "";                    // auto-pick best ping
            PhotonNetwork.NickName = "Heir-" + UnityEngine.Random.Range(100, 999);
            PhotonNetwork.AutomaticallySyncScene = false;

            if (PhotonNetwork.IsConnectedAndReady) JoinNamed();
            else if (!PhotonNetwork.ConnectUsingSettings())
            { _connecting = false; _onError?.Invoke("Could not start a connection."); }
        }

        void JoinNamed()
        {
            var opts = new RoomOptions { MaxPlayers = 8, PublishUserId = true };
            PhotonNetwork.JoinOrCreateRoom(_code, opts, TypedLobby.Default);
        }

        public override void OnConnectedToMaster()
        {
            if (_connecting) JoinNamed();
        }

        public override void OnJoinedRoom()
        {
            _connecting = false;
            Net.SetSeedFromCode(PhotonNetwork.CurrentRoom.Name);
            var cb = _onJoined; _onJoined = null;
            cb?.Invoke();
            Net.OnRosterChanged?.Invoke();
        }

        public override void OnPlayerEnteredRoom(Player p) => Net.OnRosterChanged?.Invoke();

        public override void OnPlayerLeftRoom(Player p)
        {
            Net.OnLeft?.Invoke(p.ActorNumber);
            Net.OnRosterChanged?.Invoke();
        }

        public override void OnJoinRoomFailed(short code, string msg)
        {
            _connecting = false;
            _onError?.Invoke("Couldn't join: " + msg);
        }

        public override void OnDisconnected(DisconnectCause cause)
        {
            if (_connecting)
            {
                _connecting = false;
                _onError?.Invoke("Disconnected: " + cause);
            }
        }

        public void LeaveAll()
        {
            if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom();
        }

        public void OnEvent(EventData e)
        {
            if (e.Code == Net.EvState)
            {
                var d = (object[])e.CustomData;
                Net.OnState?.Invoke(e.Sender,
                    new Vector3((float)d[0], (float)d[1], 0f), (bool)d[2]);
            }
            else if (e.Code == Net.EvWin)
            {
                Net.OnWin?.Invoke(e.Sender);
            }
        }
    }
}
#else
namespace TrustIssues
{
    // Inert stub used until PUN 2 is imported. Keeps GameRoot compiling and the
    // VERSUS button informs the player the package is missing.
    public static class Net
    {
        public const string AppId = "14e6b323-104c-45ba-95e3-0e023ee33dcf";
        public static bool Available => false;
        public static bool InRoom => false;
        public static string RoomCode => "";
        public static int LocalActor => 0;
        public static int PlayerCount => 0;
        public static int Seed => 0;

        public static Action<int, Vector3, bool> OnState;
        public static Action<int> OnLeft;
        public static Action<int> OnWin;
        public static Action OnRosterChanged;

        public static void Host(Action onJoined, Action<string> onError) => onError?.Invoke("Import Photon PUN 2 to enable multiplayer.");
        public static void Join(string code, Action onJoined, Action<string> onError) => onError?.Invoke("Import Photon PUN 2 to enable multiplayer.");
        public static void Leave() { }
        public static void SendState(Vector3 pos, bool faceLeft) { }
        public static void SendWin() { }
    }
}
#endif
