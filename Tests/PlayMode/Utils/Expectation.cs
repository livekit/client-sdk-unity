using System.Collections;
using UnityEngine;

namespace LiveKit.PlayModeTests.Utils
{
    public class Expectation
    {
        private float _timeout;
        private float _startTime;
        private State _state = State.Pending;
        private string _failureReason;
        private System.Func<bool> _predicate;

        public string Error => _state switch
        {
            State.Failed => _failureReason ?? "Expectation failed",
            State.TimedOut => $"Expectation timed out ({_timeout}s)",
            _ => null
        };

        private enum State
        {
            Pending,
            Fulfilled,
            Failed,
            TimedOut
        }

        public Expectation(System.Func<bool> predicate = null, float timeoutSeconds = 5f)
        {
            _timeout = timeoutSeconds;
            _predicate = predicate;
        }

        public void Fulfill()
        {
            _state = State.Fulfilled;
        }

        public void Fail(string reason)
        {
            _state = State.Failed;
            _failureReason = reason;
        }

        public IEnumerator Wait()
        {
            _startTime = Time.time;
            yield return new WaitUntil(() =>
            {
                if (_state != State.Pending) return true;
                if (Time.time - _startTime > _timeout)
                {
                    _state = State.TimedOut;
                    return true;
                }
                if (_predicate != null && _predicate())
                {
                    _state = State.Fulfilled;
                    return true;
                }
                return false;
            });
            if (_state == State.Pending) _state = State.TimedOut;
        }
    }
}
