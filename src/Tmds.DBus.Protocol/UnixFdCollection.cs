using System.Collections;

namespace Tmds.DBus.Protocol;

sealed class UnixFdCollection : IReadOnlyList<SafeHandle>, IDisposable
{
    private IntPtr InvalidRawHandle => new IntPtr(-1);

    private readonly List<(SafeHandle? Handle, bool CanRead)>? _handles;
    private readonly List<(IntPtr RawHandle, bool CanRead)>? _rawHandles;

    // The gate guards someone removing handles while the UnixFdCollection gets disposed by the message.
    // We don't need to lock it while adding handles, or reading them to send them.
    private bool _disposed;
    private bool _handlesReffed;

    internal object SyncObject { get; }

    internal bool IsRawHandleCollection => _rawHandles is not null;

    internal UnixFdCollection(bool isRawHandleCollection = true)
    {
        if (isRawHandleCollection)
        {
            SyncObject = _rawHandles = new();
        }
        else
        {
            SyncObject = _handles = new();
        }
    }

    internal int AddHandle(IntPtr handle)
    {
        _rawHandles!.Add((handle, true));
        return _rawHandles.Count - 1;
    }

    internal void AddHandle(SafeHandle handle)
    {
        Debug.Assert(!_handlesReffed);
        if (handle is null)
        {
            throw new ArgumentNullException(nameof(handle));
        }
        _handles!.Add((handle, true));
    }

    public int Count => _rawHandles is not null ? _rawHandles.Count : _handles!.Count;

    // Used to get the file descriptors to send them over the socket.
    public SafeHandle this[int index] => _handles![index].Handle!;

    // We remain responsible for disposing the handle.
    public IntPtr ReadHandleRaw(int index)
    {
        lock (SyncObject)
        {
            if (_disposed)
            {
                ThrowDisposed();
            }
            if (_rawHandles is not null)
            {
                (IntPtr rawHandle, bool CanRead) = _rawHandles[index];
                if (!CanRead)
                {
                    ThrowHandleAlreadyRead();
                }
                // Handle can no longer be read, but we are still responible for disposing it.
                _rawHandles[index] = (rawHandle, false);
                return rawHandle;
            }
            else
            {
                Debug.Assert(_handles is not null);
                (SafeHandle? handle, bool CanRead) = _handles![index];
                if (!CanRead)
                {
                    ThrowHandleAlreadyRead();
                }
                // Handle can no longer be read, but we are still responible for disposing it.
                _handles[index] = (handle, false);
                return handle!.DangerousGetHandle();
            }
        }
    }

    private void ThrowHandleAlreadyRead()
    {
        throw new InvalidOperationException("The handle was already read.");
    }

    private void ThrowDisposed()
    {
        throw new ObjectDisposedException(typeof(UnixFdCollection).FullName);
    }

    public T? ReadHandle<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]T>(int index) where T : SafeHandle, new()
        => ReadHandleGeneric<T>(index);

    // The caller of this method owns the handle and is responsible for Disposing it.
    internal T? ReadHandleGeneric<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]T>(int index)
    {
        lock (SyncObject)
        {
            if (_disposed)
            {
                ThrowDisposed();
            }
            if (_rawHandles is not null)
            {
                (IntPtr rawHandle, bool CanRead) = _rawHandles[index];
                if (!CanRead)
                {
                    ThrowHandleAlreadyRead();
                }
    #if NET6_0_OR_GREATER
                SafeHandle handle = (Activator.CreateInstance<T>() as SafeHandle)!;
                Marshal.InitHandle(handle, rawHandle);
    #else
                SafeHandle? handle = (SafeHandle?)Activator.CreateInstance(typeof(T), new object[] { rawHandle, true });
    #endif
                _rawHandles[index] = (InvalidRawHandle, false);
                return (T?)(object?)handle;
            }
            else
            {
                Debug.Assert(_handles is not null);
                (SafeHandle? handle, bool CanRead) = _handles![index];
                if (!CanRead)
                {
                    ThrowHandleAlreadyRead();
                }
                if (handle is not T)
                {
                    throw new ArgumentException($"Requested handle type {typeof(T).FullName} does not matched stored type {handle?.GetType().FullName}.");
                }
                _handles[index] = (null, false);
                return (T)(object)handle;
            }
        }
    }

    public IEnumerator<SafeHandle> GetEnumerator()
    {
        throw new NotSupportedException();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        throw new NotSupportedException();
    }

    public void Dispose()
    {
        lock (SyncObject)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            DisposeHandles(disposing: true);
        }

        GC.SuppressFinalize(this);
    }

    internal void RefHandles()
    {
        lock (SyncObject)
        {
            if (_disposed)
            {
                ThrowDisposed();
            }

            int handleRefsAdded = 0;
            try
            {
                for (int i = 0; i < Count; i++)
                {
                    bool added = false;
                    SafeHandle h = this[i];
                    h.DangerousAddRef(ref added);
                    handleRefsAdded++;
                }

                _handlesReffed = true;
            }
            catch
            {
                for (int i = 0; i < handleRefsAdded; i++)
                {
                    SafeHandle h = this[i];
                    h.DangerousRelease();
                }

                throw;
            }
        }
    }

    internal int DangerousGetHandle(int i)
    {
        Debug.Assert(Monitor.IsEntered(SyncObject));
        if (!_handlesReffed)
        {
            throw new InvalidOperationException("Trying to send an unreffed handle.");
        }
        int fd = this[i].DangerousGetHandle().ToInt32();
        return fd;
    }

    ~UnixFdCollection()
    {
        DisposeHandles(false);
    }

    private void DisposeHandles(bool disposing)
    {
        bool handlesReffed = _handlesReffed;
        if (handlesReffed)
        {
            _handlesReffed = false;

            for (int i = 0; i < Count; i++)
            {
                SafeHandle h = this[i];
                h.DangerousRelease();
            }
        }

        if (disposing || handlesReffed)
        {
            // dispose managed state
            if (_handles is not null)
            {
                for (int i = 0; i < Count; i++)
                {
                    var handle = _handles[i];
                    if (handle.Handle is not null)
                    {
                        handle.Handle.Dispose();
                    }
                }
                _handles.Clear();
            }
        }

        // free unmanaged resources
        if (_rawHandles is not null)
        {
            for (int i = 0; i < Count; i++)
            {
                var handle = _rawHandles[i];

                if (handle.RawHandle != InvalidRawHandle)
                {
                    close(handle.RawHandle.ToInt32());
                }
            }
            _rawHandles.Clear();
        }
    }

    [DllImport("libc")]
    private static extern void close(int fd);

    internal void MoveTo(UnixFdCollection handles, int count)
    {
        if (handles.IsRawHandleCollection != IsRawHandleCollection)
        {
            throw new ArgumentException("Handle collections are not compatible.");
        }
        if (handles.IsRawHandleCollection)
        {
            for (int i = 0; i < count; i++)
            {
                handles._rawHandles!.Add(_rawHandles![i]);
            }
            _rawHandles!.RemoveRange(0, count);
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                handles._handles!.Add(_handles![i]);
            }
            _handles!.RemoveRange(0, count);
        }
    }
}