using System;
using Inputs;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.Editor
{
    public class ActionStackTests
    {
        [Test]
        public void ActionStack_AddAction_InvokesAction()
        {
            bool invoked = false;
            ActionStack stack = new ActionStack();
            
            Action action = () => invoked = true;
            stack += action;
            
            stack.Invoke();
            
            Assert.IsTrue(invoked);
        }

        [Test]
        public void ActionStack_AddMultipleActions_InvokesLIFO()
        {
            int invokeOrder = 0;
            int action1Order = -1;
            int action2Order = -1;

            ActionStack stack = new ActionStack();
            
            Action action1 = () => action1Order = ++invokeOrder;
            Action action2 = () => action2Order = ++invokeOrder;

            stack += action1;
            stack += action2;
            
            // First invoke should pop and run action2
            stack.Invoke();
            // Since count becomes 1, the next invoke just peeks
            stack.Invoke();
            
            Assert.AreEqual(2, action1Order, "Action 1 should be invoked second (after popped)");
            Assert.AreEqual(1, action2Order, "Action 2 should be invoked first (LIFO)");
        }

        [Test]
        public void ActionStack_RemoveAction_DoesNotInvokeRemoved()
        {
            bool invoked = false;
            ActionStack stack = new ActionStack();
            
            Action action = () => invoked = true;
            stack += action;
            stack -= action;
            
            stack.Invoke();
            
            Assert.IsFalse(invoked);
        }

        [Test]
        public void ActionStackGeneric_AddAction_InvokesWithArgument()
        {
            string receivedArg = "";
            ActionStack<string> stack = new ActionStack<string>();
            
            Action<string> action = (arg) => receivedArg = arg;
            stack += action;
            
            stack.Invoke("Test");
            
            Assert.AreEqual("Test", receivedArg);
        }

        [Test]
        public void ActionStack_ExceptionInAction_LogsErrorButDoesNotCrash()
        {
            ActionStack stack = new ActionStack();
            stack += () => throw new Exception("Test Exception");
            
            // Note: We need to be careful with the exact string match including newlines/traces
            // Or use a regex.
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Error invoking action from ActionStack: System.Exception: Test Exception"));
            
            Assert.DoesNotThrow(() => stack.Invoke());
        }
    }
}
