using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LiveKit;

public class RoomManager : MonoBehaviour
{
    private Room _room;

    [SerializeField]
    private string _serverUrl = "ws://localhost:7880";

    [SerializeField]
    private string _token = "some token";

    void Start()
    {
        StartCoroutine(Connect());
    }

    private IEnumerator Connect()
    {
        _room = new Room();
        var options = new RoomOptions();
        var connect = _room.Connect(_serverUrl, _token, options);
        yield return connect;

        if (connect.IsError)
        {
            Debug.LogError($"Failed to connect to room");
            yield break;
        }
        Debug.Log("Connected to room!");
    }
}
