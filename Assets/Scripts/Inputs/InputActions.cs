using System;
using UnityEngine.InputSystem;

namespace Controllers.Inputs
{
    public class InputActions
    {
        public Action onInputStarted;
        public Action onInputCanceled;
        public Action onInputPerformed;

        public static void InvokeInputAction(InputActions _inputActions, InputAction.CallbackContext _context)
        {
            switch (_context.phase)
            {
                case InputActionPhase.Started:
                    _inputActions.onInputStarted?.Invoke();
                    break;
                case InputActionPhase.Canceled:
                    _inputActions.onInputCanceled?.Invoke();
                    break;
                case InputActionPhase.Performed:
                    _inputActions.onInputPerformed?.Invoke();
                    break;
            }
        }
    }
    
    public class InputActions<T>
    {
        public Action<T> onInputStarted;
        public Action<T> onInputCanceled;
        public Action<T> onInputPerformed;
    }
    
    public class InputActions<T1, T2>
    {
        public Action<T1, T2> onInputStarted;
        public Action<T1, T2> onInputCanceled;
        public Action<T1, T2> onInputPerformed;
    }
}