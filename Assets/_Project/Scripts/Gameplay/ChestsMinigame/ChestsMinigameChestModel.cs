using System;

namespace Company.ChestGame.Minigame.Chests.Internal
{
    public class ChestsMinigameChestModel
    {
        public enum State
        {
            Closed,
            Opening,
            Open_Empty,
            Open_Prize
        }

        public event Action<State> OnStateChanged;
        public State CurrentState
        {
            get
            {
                return _state;
            }

            private set
            {
                _state = value;
                OnStateChanged?.Invoke(_state);
            }
        }

        public float Completition { get; private set; }

        private State _state = State.Closed;

        public void SetClosed()
        {
            if (CurrentState == State.Closed) return;

            Completition = 0;
            CurrentState = State.Closed;
        }
        public void SetOpening(float completition)
        {
            Completition = completition;

            CurrentState = State.Opening;
        }

        public void SetOpen(bool hasPrize)
        {
            if (CurrentState == State.Open_Empty || CurrentState == State.Open_Prize) return;

            Completition = 1;
            CurrentState = hasPrize ? State.Open_Prize : State.Open_Empty;
        }

    }
}