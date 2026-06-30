using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace AgentsRPG
{
    public class ChatBubble : MonoBehaviour
    {
        [SerializeField] TMP_Text _text;
        [SerializeField] float _streamCharacterDelay = 0.03f;

        public event Action TextChanged;

        readonly Queue<char> _pendingChars = new Queue<char>();
        Coroutine _drainCoroutine;

        public void StreamText(string chunk)
        {
            if (string.IsNullOrEmpty(chunk)) return;
            foreach (var c in chunk) _pendingChars.Enqueue(c);
            if (_drainCoroutine == null)
            {
                _drainCoroutine = StartCoroutine(Drain());
            }
        }

        IEnumerator Drain()
        {
            while (_pendingChars.Count > 0)
            {
                _text.text += _pendingChars.Dequeue();
                TextChanged?.Invoke();
                if (_streamCharacterDelay > 0f)
                    yield return new WaitForSeconds(_streamCharacterDelay);
                else
                    yield return null;
            }
            _drainCoroutine = null;
        }

        void StopStreaming()
        {
            if (_drainCoroutine != null)
            {
                StopCoroutine(_drainCoroutine);
                _drainCoroutine = null;
            }
            _pendingChars.Clear();
        }

        void OnDisable()
        {
            StopStreaming();
        }
    }
}
