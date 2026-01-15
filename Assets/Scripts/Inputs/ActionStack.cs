using System;
using System.Collections.Generic;
using Debug = UnityEngine.Debug;

namespace Inputs
{
    public class ActionStack
    {
        private Stack<Action> actions = new();
    
        public static ActionStack operator +(ActionStack stack, Action action)
        {
            stack.actions.Push(action);
            return stack;
        }
    
        public static ActionStack operator -(ActionStack stack, Action action)
        {
            if (stack.actions.Count > 0)
            {
                Stack<Action> temp = new Stack<Action>();
                bool found = false;
            
                while (stack.actions.Count > 0)
                {
                    Action current = stack.actions.Pop();
                    if (!found && current == action)
                    {
                        found = true;
                        continue;
                    }
                    temp.Push(current);
                }
            
                while (temp.Count > 0)
                {
                    stack.actions.Push(temp.Pop());
                }
            }
            return stack;
        }
    
        public void Invoke()
        {
            try
            {
                if (actions.Count == 1)
                {
                    actions.Peek()?.Invoke();
                }
                else if (actions.Count > 1)
                {
                    Action topAction = actions.Pop();
                    topAction?.Invoke();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error invoking action from ActionStack: {e}");
            }
        }
    }

    public class ActionStack<T>
    {
        private Stack<Action<T>> actions = new();
    
        public static ActionStack<T> operator +(ActionStack<T> stack, Action<T> action)
        {
            stack.actions.Push(action);
            return stack;
        }
    
        public static ActionStack<T> operator -(ActionStack<T> stack, Action<T> action)
        {
            if (stack.actions.Count > 0)
            {
                Stack<Action<T>> temp = new Stack<Action<T>>();
                bool found = false;
            
                while (stack.actions.Count > 0)
                {
                    Action<T> current = stack.actions.Pop();
                    if (!found && current == action)
                    {
                        found = true;
                        continue;
                    }
                    temp.Push(current);
                }
            
                while (temp.Count > 0)
                {
                    stack.actions.Push(temp.Pop());
                }
            }
            return stack;
        }
    
        public void Invoke(T arg)
        {
            try
            {
                if (actions.Count == 1)
                {
                    actions.Peek()?.Invoke(arg);
                }
                else if (actions.Count > 1)
                {
                    Action<T> topAction = actions.Pop();
                    topAction?.Invoke(arg);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error invoking action from ActionStack: {e}");
            }
        }
    }
}