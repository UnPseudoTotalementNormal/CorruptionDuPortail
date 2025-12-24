using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Network.Action
{
    public class NetworkAction
    {
        private string messageID;
        private List<System.Action> listeners = new();
        
        public bool isRegistered { get; private set; }
        
        public bool allowInvokeByClients { get; }
        
        public NetworkAction(string _messageID, bool _allowInvokeByClients = true)
        {
            messageID = _messageID;
            allowInvokeByClients = _allowInvokeByClients;
            Register();
        }

        #region Invoke

        public void Invoke()
        {
            using FastBufferWriter _writer = new(1, Unity.Collections.Allocator.Temp);
            
            if (NetworkManager.Singleton.IsServer)
            {
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll(messageID, _writer);
            }
            else
            {
                if (!allowInvokeByClients)
                {
                    Debug.LogWarning("Client attempted to invoke NetworkAction: " + messageID + ", but client invocation is not allowed.");
                    return;
                }
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(messageID, NetworkManager.ServerClientId, _writer);
            }
        }
        
        private void OnReceiveMessage(ulong _senderClientId, FastBufferReader _messagePayload)
        {
            if (NetworkManager.Singleton.IsServer && _senderClientId != NetworkManager.ServerClientId)
            {
                if (!allowInvokeByClients)
                {
                    Debug.LogWarning("Client attempted to invoke NetworkAction: " + messageID + ", but client invocation is not allowed.");
                    return;
                }
                
                FastBufferWriter _writer = new FastBufferWriter(1, Unity.Collections.Allocator.Temp);
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll(messageID, _writer);
                return; // Early return to avoid invoking listeners twice on the server
            }
            
            foreach (var _listener in listeners)
            {
                _listener.Invoke();
            }
        }

        #endregion

        #region Listener management

        public void AddListener(System.Action _listener)
        {
            if (listeners.Contains(_listener))
            {
                Debug.Log("Listener already added to NetworkAction: " + messageID + ". Ignoring.");
                return;
            }
            listeners.Add(_listener);
        }
        
        public void RemoveListener(System.Action _listener)
        {
            if (!listeners.Contains(_listener))
            {
                Debug.Log("Listener not found in NetworkAction: " + messageID + ". Ignoring.");
                return;
            }
            listeners.Remove(_listener);
        }
        
        public static NetworkAction operator +(NetworkAction _action, System.Action _listener)
        {
            _action.AddListener(_listener);
            return _action;
        }
        
        public static NetworkAction operator -(NetworkAction _action, System.Action _listener)
        {
            _action.RemoveListener(_listener);
            return _action;
        }
        
        public static implicit operator bool(NetworkAction _action)
        {
            return _action != null && _action.isRegistered && _action.listeners.Count > 0;
        }

        #endregion

        #region Message registration

        public void Register()
        {
            if (isRegistered)
            {
                Debug.Log("NetworkAction: " + messageID + " is already registered. Ignoring.");
                return;
            }
            
            NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(messageID, OnReceiveMessage);
            isRegistered = true;
        }
        
        public void Unregister()
        {
            if (!isRegistered)
            {
                Debug.Log("NetworkAction: " + messageID + " is not registered. Ignoring.");
                return;
            }
            
            NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(messageID);
            isRegistered = false;
        }

        #endregion
    }

    public class NetworkAction<T>
    {
        private string messageID;
        private List<System.Action<T>> listeners = new();
        private INetworkActionSerializer<T> serializer;

        public bool isRegistered { get; private set; }
        
        public bool allowInvokeByClients { get; }

        public NetworkAction(string _messageID, bool _allowInvokeByClients = true)
        {
            messageID = _messageID;
            serializer = NetworkActionSerializerFactory.GetSerializer<T>();
            allowInvokeByClients = _allowInvokeByClients;
            Register();
        }

        #region Invoke

        public void Invoke(T _param)
        {
            FastBufferWriter _writer = new FastBufferWriter(128, Unity.Collections.Allocator.Temp);
            try
            {
                serializer.Serialize(_writer, _param);
                
                if (NetworkManager.Singleton.IsServer)
                {
                    NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll(messageID, _writer);
                }
                else
                {
                    if (!allowInvokeByClients)
                    {
                        Debug.LogWarning("Client attempted to invoke NetworkAction: " + messageID + ", but client invocation is not allowed.");
                        return;
                    }
                    NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(messageID, NetworkManager.ServerClientId, _writer);
                }
            }
            finally
            {
                _writer.Dispose();
            }
        }

        private void OnReceiveMessage(ulong _senderClientId, FastBufferReader _messagePayload)
        {
            T _param = serializer.Deserialize(_messagePayload);
            
            if (NetworkManager.Singleton.IsServer && _senderClientId != NetworkManager.ServerClientId)
            {
                if (!allowInvokeByClients)
                {
                    Debug.LogWarning("Client attempted to invoke NetworkAction: " + messageID + ", but client invocation is not allowed.");
                    return;
                }
                
                FastBufferWriter _writer = new FastBufferWriter(128, Unity.Collections.Allocator.Temp);
                try
                {
                    serializer.Serialize(_writer, _param);
                    NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll(messageID, _writer);
                }
                finally
                {
                    _writer.Dispose();
                }
                return; // Early return to avoid invoking listeners twice on the server
            }
            
            foreach (var _listener in listeners)
            {
                _listener.Invoke(_param);
            }
        }

        #endregion

        #region Listener management

        public void AddListener(System.Action<T> _listener)
        {
            if (listeners.Contains(_listener))
            {
                Debug.Log("Listener already added to NetworkAction: " + messageID + ". Ignoring.");
                return;
            }

            listeners.Add(_listener);
        }

        public void RemoveListener(System.Action<T> _listener)
        {
            if (!listeners.Contains(_listener))
            {
                Debug.Log("Listener not found in NetworkAction: " + messageID + ". Ignoring.");
                return;
            }

            listeners.Remove(_listener);
        }

        public static NetworkAction<T> operator +(NetworkAction<T> _action, System.Action<T> _listener)
        {
            _action.AddListener(_listener);
            return _action;
        }

        public static NetworkAction<T> operator -(NetworkAction<T> _action, System.Action<T> _listener)
        {
            _action.RemoveListener(_listener);
            return _action;
        }

        public static implicit operator bool(NetworkAction<T> _action)
        {
            return _action != null && _action.isRegistered && _action.listeners.Count > 0;
        }

        #endregion

        #region Message registration

        public void Register()
        {
            if (isRegistered)
            {
                Debug.Log("NetworkAction: " + messageID + " is already registered. Ignoring.");
                return;
            }

            NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(messageID, OnReceiveMessage);
            isRegistered = true;
        }
        
        public void Unregister()
        {
            if (!isRegistered)
            {
                Debug.Log("NetworkAction: " + messageID + " is not registered. Ignoring.");
                return;
            }
            
            NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(messageID);
            isRegistered = false;
        }
        
        #endregion
    }

    #region Serialization System

    internal interface INetworkActionSerializer<T>
    {
        void Serialize(FastBufferWriter _writer, T _value);
        T Deserialize(FastBufferReader _reader);
    }

    internal static class NetworkActionSerializerFactory
    {
        public static INetworkActionSerializer<T> GetSerializer<T>()
        {
            var _type = typeof(T);

            // Primitive types
            if (_type == typeof(int))
                return (INetworkActionSerializer<T>)new IntSerializer();
            if (_type == typeof(uint))
                return (INetworkActionSerializer<T>)new UIntSerializer();
            if (_type == typeof(long))
                return (INetworkActionSerializer<T>)new LongSerializer();
            if (_type == typeof(ulong))
                return (INetworkActionSerializer<T>)new ULongSerializer();
            if (_type == typeof(short))
                return (INetworkActionSerializer<T>)new ShortSerializer();
            if (_type == typeof(ushort))
                return (INetworkActionSerializer<T>)new UShortSerializer();
            if (_type == typeof(byte))
                return (INetworkActionSerializer<T>)new ByteSerializer();
            if (_type == typeof(sbyte))
                return (INetworkActionSerializer<T>)new SByteSerializer();
            if (_type == typeof(bool))
                return (INetworkActionSerializer<T>)new BoolSerializer();
            if (_type == typeof(float))
                return (INetworkActionSerializer<T>)new FloatSerializer();
            if (_type == typeof(double))
                return (INetworkActionSerializer<T>)new DoubleSerializer();
            if (_type == typeof(string))
                return (INetworkActionSerializer<T>)new StringSerializer();
            if (_type == typeof(Vector2))
                return (INetworkActionSerializer<T>)new Vector2Serializer();
            if (_type == typeof(Vector3))
                return (INetworkActionSerializer<T>)new Vector3Serializer();
            if (_type == typeof(Vector4))
                return (INetworkActionSerializer<T>)new Vector4Serializer();
            if (_type == typeof(Quaternion))
                return (INetworkActionSerializer<T>)new QuaternionSerializer();
            if (_type == typeof(Color))
                return (INetworkActionSerializer<T>)new ColorSerializer();
            if (_type == typeof(Color32))
                return (INetworkActionSerializer<T>)new Color32Serializer();

            // INetworkSerializable
            if (typeof(INetworkSerializable).IsAssignableFrom(_type))
            {
                var _serializerType = typeof(NetworkSerializableSerializer<>).MakeGenericType(_type);
                return (INetworkActionSerializer<T>)System.Activator.CreateInstance(_serializerType);
            }

            throw new System.NotSupportedException(
                $"Type {_type.Name} n'est pas supporté par NetworkAction. " +
                "Le type doit être soit un type primitif (int, float, string, etc.), " +
                "soit implémenter INetworkSerializable.");
        }
    }

    // Serializers for primitive types
    internal class IntSerializer : INetworkActionSerializer<int>
    {
        public void Serialize(FastBufferWriter _writer, int _value) => _writer.WriteValueSafe(_value);
        public int Deserialize(FastBufferReader _reader) { _reader.ReadValueSafe(out int _value); return _value; }
    }

    internal class UIntSerializer : INetworkActionSerializer<uint>
    {
        public void Serialize(FastBufferWriter _writer, uint _value) => _writer.WriteValueSafe(_value);
        public uint Deserialize(FastBufferReader _reader) { _reader.ReadValueSafe(out uint _value); return _value; }
    }

    internal class LongSerializer : INetworkActionSerializer<long>
    {
        public void Serialize(FastBufferWriter _writer, long _value) => _writer.WriteValueSafe(_value);
        public long Deserialize(FastBufferReader _reader) { _reader.ReadValueSafe(out long _value); return _value; }
    }

    internal class ULongSerializer : INetworkActionSerializer<ulong>
    {
        public void Serialize(FastBufferWriter _writer, ulong _value) => _writer.WriteValueSafe(_value);
        public ulong Deserialize(FastBufferReader _reader) { _reader.ReadValueSafe(out ulong _value); return _value; }
    }

    internal class ShortSerializer : INetworkActionSerializer<short>
    {
        public void Serialize(FastBufferWriter _writer, short _value) => _writer.WriteValueSafe(_value);
        public short Deserialize(FastBufferReader _reader) { _reader.ReadValueSafe(out short _value); return _value; }
    }

    internal class UShortSerializer : INetworkActionSerializer<ushort>
    {
        public void Serialize(FastBufferWriter _writer, ushort _value) => _writer.WriteValueSafe(_value);
        public ushort Deserialize(FastBufferReader _reader) { _reader.ReadValueSafe(out ushort _value); return _value; }
    }

    internal class ByteSerializer : INetworkActionSerializer<byte>
    {
        public void Serialize(FastBufferWriter _writer, byte _value) => _writer.WriteValueSafe(_value);
        public byte Deserialize(FastBufferReader _reader) { _reader.ReadValueSafe(out byte _value); return _value; }
    }

    internal class SByteSerializer : INetworkActionSerializer<sbyte>
    {
        public void Serialize(FastBufferWriter _writer, sbyte _value) => _writer.WriteValueSafe(_value);
        public sbyte Deserialize(FastBufferReader _reader) { _reader.ReadValueSafe(out sbyte _value); return _value; }
    }

    internal class BoolSerializer : INetworkActionSerializer<bool>
    {
        public void Serialize(FastBufferWriter _writer, bool _value) => _writer.WriteValueSafe(_value);
        public bool Deserialize(FastBufferReader _reader) { _reader.ReadValueSafe(out bool _value); return _value; }
    }

    internal class FloatSerializer : INetworkActionSerializer<float>
    {
        public void Serialize(FastBufferWriter _writer, float _value) => _writer.WriteValueSafe(_value);
        public float Deserialize(FastBufferReader _reader) { _reader.ReadValueSafe(out float _value); return _value; }
    }

    internal class DoubleSerializer : INetworkActionSerializer<double>
    {
        public void Serialize(FastBufferWriter _writer, double _value) => _writer.WriteValueSafe(_value);
        public double Deserialize(FastBufferReader _reader) { _reader.ReadValueSafe(out double _value); return _value; }
    }

    internal class StringSerializer : INetworkActionSerializer<string>
    {
        public void Serialize(FastBufferWriter _writer, string _value) => _writer.WriteValueSafe(_value);
        public string Deserialize(FastBufferReader _reader) { _reader.ReadValueSafe(out string _value); return _value; }
    }

    internal class Vector2Serializer : INetworkActionSerializer<Vector2>
    {
        public void Serialize(FastBufferWriter _writer, Vector2 _value) => _writer.WriteValueSafe(_value);
        public Vector2 Deserialize(FastBufferReader _reader) { _reader.ReadValueSafe(out Vector2 _value); return _value; }
    }

    internal class Vector3Serializer : INetworkActionSerializer<Vector3>
    {
        public void Serialize(FastBufferWriter _writer, Vector3 _value) => _writer.WriteValueSafe(_value);
        public Vector3 Deserialize(FastBufferReader _reader) { _reader.ReadValueSafe(out Vector3 _value); return _value; }
    }

    internal class Vector4Serializer : INetworkActionSerializer<Vector4>
    {
        public void Serialize(FastBufferWriter _writer, Vector4 _value) => _writer.WriteValueSafe(_value);
        public Vector4 Deserialize(FastBufferReader _reader) { _reader.ReadValueSafe(out Vector4 _value); return _value; }
    }

    internal class QuaternionSerializer : INetworkActionSerializer<Quaternion>
    {
        public void Serialize(FastBufferWriter _writer, Quaternion _value) => _writer.WriteValueSafe(_value);
        public Quaternion Deserialize(FastBufferReader _reader) { _reader.ReadValueSafe(out Quaternion _value); return _value; }
    }

    internal class ColorSerializer : INetworkActionSerializer<Color>
    {
        public void Serialize(FastBufferWriter _writer, Color _value) => _writer.WriteValueSafe(_value);
        public Color Deserialize(FastBufferReader _reader) { _reader.ReadValueSafe(out Color _value); return _value; }
    }

    internal class Color32Serializer : INetworkActionSerializer<Color32>
    {
        public void Serialize(FastBufferWriter _writer, Color32 _value) => _writer.WriteValueSafe(_value);
        public Color32 Deserialize(FastBufferReader _reader) { _reader.ReadValueSafe(out Color32 _value); return _value; }
    }

    // Serializer for INetworkSerializable
    internal class NetworkSerializableSerializer<T> : INetworkActionSerializer<T> where T : INetworkSerializable, new()
    {
        public void Serialize(FastBufferWriter _writer, T _value)
        {
            _writer.WriteNetworkSerializable(_value);
        }

        public T Deserialize(FastBufferReader _reader)
        {
            _reader.ReadNetworkSerializable(out T _value);
            return _value;
        }
    }

    #endregion
}