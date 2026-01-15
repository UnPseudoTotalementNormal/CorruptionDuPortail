using System;
using ChatSystem;
using Controllers;
using Controllers.Inputs;
using Inputs;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{
    public static InputManager instance;

    public InputActions arrowUpInputActions = new();
    public InputActions arrowDownInputActions = new();
    public InputActions arrowLeftInputActions = new();
    public InputActions arrowRightInputActions = new();
    
    public ActionStack onEscapePressedStack = new();

    private void Awake()
    {
        instance = this;
    }

    #region Register/Unregister Actions

    public void RegisterAction(InputID inputID, InputState inputState, Action callback)
    {
        InputActions inputActions = GetInputActions(inputID);
        if (inputActions == null)
        {
            ActionStack actionStack = GetActionStack(inputID);

            if (actionStack == null)
            {
                Debug.LogWarning("InputManager: No actions or action stack found for InputID " + inputID);
                return;
            }
            
            actionStack += callback;
            
            return;
        }

        
        switch (inputState)
        {
            case InputState.Started:
                inputActions.onInputStarted += callback;
                break;
            case InputState.Canceled:
                inputActions.onInputCanceled += callback;
                break;
            case InputState.Performed:
                inputActions.onInputPerformed += callback;
                break;
        }
    }

    public void UnregisterAction(InputID inputID, InputState inputState, Action callback)
    {
        InputActions inputActions = GetInputActions(inputID);
        if (inputActions == null)
        {
            ActionStack actionStack = GetActionStack(inputID);

            if (actionStack == null)
            {
                Debug.LogWarning("InputManager: No actions or action stack found for InputID " + inputID);
                return;
            }
            
            actionStack -= callback;

            return;
        }
        
        switch (inputState)
        {
            case InputState.Started:
                inputActions.onInputStarted -= callback;
                break;
            case InputState.Canceled:
                inputActions.onInputCanceled -= callback;
                break;
            case InputState.Performed:
                inputActions.onInputPerformed -= callback;
                break;
        }
    }

    public void RegisterAction<T>(InputID inputID, InputState inputState, Action<T> callback)
    {
        InputActions<T> inputActions = GetInputActions<T>(inputID);
        if (inputActions == null)
        {
            ActionStack<T> actionStack = GetActionStack<T>(inputID);

            if (actionStack == null)
            {
                Debug.LogWarning("InputManager: No actions or action stack found for InputID " + inputID);
                return;
            }
            
            actionStack += callback;

            return;
        }
        
        switch (inputState)
        {
            case InputState.Started:
                inputActions.onInputStarted += callback;
                break;
            case InputState.Canceled:
                inputActions.onInputCanceled += callback;
                break;
            case InputState.Performed:
                inputActions.onInputPerformed += callback;
                break;
        }
    }

    public void UnregisterAction<T>(InputID inputID, InputState inputState, Action<T> callback)
    {
        InputActions<T> inputActions = GetInputActions<T>(inputID);
        if (inputActions == null)
        {
            ActionStack<T> actionStack = GetActionStack<T>(inputID);

            if (actionStack == null)
            {
                Debug.LogWarning("InputManager: No actions or action stack found for InputID " + inputID);
                return;
            }
            
            actionStack -= callback;

            return;
        }
        
        switch (inputState)
        {
            case InputState.Started:
                inputActions.onInputStarted -= callback;
                break;
            case InputState.Canceled:
                inputActions.onInputCanceled -= callback;
                break;
            case InputState.Performed:
                inputActions.onInputPerformed -= callback;
                break;
        }
    }

    private InputActions GetInputActions(InputID inputID)
    {
        return inputID switch
        {
            InputID.ArrowUp => arrowUpInputActions,
            InputID.ArrowDown => arrowDownInputActions,
            InputID.ArrowLeft => arrowLeftInputActions,
            InputID.ArrowRight => arrowRightInputActions,
            _ => null
        };
    }
    
    private ActionStack GetActionStack(InputID inputID)
    {
        return inputID switch
        {
            InputID.EscapePressedStack => onEscapePressedStack,
            _ => null
        };
    }

    private ActionStack<T> GetActionStack<T>(InputID inputID)
    {
        return null; // Currently, there are no ActionStack<T> instances defined
    }

    private InputActions<T> GetInputActions<T>(InputID inputID)
    {
        object actions = inputID switch
        {
            _ => null
        };
        
        return actions as InputActions<T>;
    }
    

    #endregion
    
    public void OnArrowUpPressed(InputAction.CallbackContext _context)
    {
        InputActions.InvokeInputAction(arrowUpInputActions, _context);
    }
    
    public void OnArrowDownPressed(InputAction.CallbackContext _context)
    {
        InputActions.InvokeInputAction(arrowDownInputActions, _context);
    }
    
    public void OnArrowLeftPressed(InputAction.CallbackContext _context)
    {
        InputActions.InvokeInputAction(arrowLeftInputActions, _context);
    }
    
    public void OnArrowRightPressed(InputAction.CallbackContext _context)
    {
        InputActions.InvokeInputAction(arrowRightInputActions, _context);
    }
    
    public void OnEscapePressed(InputAction.CallbackContext _context)
    {
        if (_context.started)
        {
            onEscapePressedStack.Invoke();
        }
    }
}
