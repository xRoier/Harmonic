﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace SharpRtmp.Buffers;

public sealed class ByteBuffer : IDisposable
{
    private readonly List<byte[]> _buffers = new();
    private int _bufferEnd;
    private int _bufferStart;
    private readonly int _maxiumBufferSize;
    private event Action MemoryUnderLimit;
    private event Action DataWritten;
    private readonly object _sync = new object();
    private readonly ArrayPool<byte> _arrayPool;
    private int BufferSegmentSize { get; }
    public int Length => _buffers.Count * BufferSegmentSize - BufferBytesAvailable() - _bufferStart;

    public ByteBuffer(int bufferSegmentSize = 1024, int maxiumBufferSize = -1, ArrayPool<byte> arrayPool = null)
    {
        if (bufferSegmentSize == 0)
            throw new ArgumentOutOfRangeException();

        BufferSegmentSize = bufferSegmentSize;
        _maxiumBufferSize = maxiumBufferSize;
        _arrayPool = arrayPool ?? ArrayPool<byte>.Shared;
        _buffers.Add(_arrayPool.Rent(bufferSegmentSize));
    }

    private int BufferBytesAvailable() => BufferSegmentSize - _bufferEnd;

    private void AddNewBufferSegment()
    {
        var arr = _arrayPool.Rent(BufferSegmentSize);
        Debug.Assert(_buffers.IndexOf(arr) == -1);
        _buffers.Add(arr);
        _bufferEnd = 0;
    }

    public void WriteToBuffer(byte data)
    {
        if (Length > _maxiumBufferSize && _maxiumBufferSize >= 0)
            throw new InvalidOperationException("buffer length exceeded");
        lock (_sync)
        {
            var available = BufferBytesAvailable();
            byte[] buffer;
            if (available == 0)
            {
                AddNewBufferSegment();
                buffer = _buffers.Last();
            }
            else
                buffer = _buffers.Last();
            buffer[_bufferEnd] = data;
            _bufferEnd += 1;
        }
    }

    private void WriteToBufferNoCheck(ReadOnlySpan<byte> bytes)
    {
        lock (_sync)
        {
            var requiredLength = bytes.Length;
            var available = BufferBytesAvailable();
            if (available < requiredLength)
            {
                var bytesIndex = 0;
                do
                {
                    var buffer = _buffers.Last();
                    var seq = bytes.Slice(bytesIndex, Math.Min(available, requiredLength));
                    seq.CopyTo(buffer.AsSpan(_bufferEnd));
                    _bufferEnd += seq.Length;
                    requiredLength -= seq.Length;
                    available -= seq.Length;
                    bytesIndex += seq.Length;

                    if (available != 0) continue;
                    AddNewBufferSegment();
                    available = BufferBytesAvailable();
                }
                while (requiredLength != 0);
            }
            else
            {
                var buffer = _buffers.Last();
                bytes.CopyTo(buffer.AsSpan(_bufferEnd));
                _bufferEnd += bytes.Length;
            }
        }
        DataWritten?.Invoke();
    }

    private class Source : IValueTaskSource
    {
        private static readonly Action<object> CallbackCompleted = _ => { Debug.Assert(false, "Should not be invoked"); };

        private readonly List<Action> _cb = new();
        private ValueTaskSourceStatus _status = ValueTaskSourceStatus.Pending;
        private ExecutionContext _executionContext;
        private object _scheduler;
        private object _state;
        private Action<object> _continuation;

        public void Cancel()
        {
            _status = ValueTaskSourceStatus.Canceled;
        }
        public void Success()
        {
            _status = ValueTaskSourceStatus.Succeeded;
            var previousContinuation = Interlocked.CompareExchange(ref _continuation, CallbackCompleted, null);
            if (previousContinuation == null) return;
            // Async work completed, continue with... continuation
            var ec = _executionContext;
            if (ec == null)
                InvokeContinuation(previousContinuation, _state, forceAsync: false);
            else
            {
                // This case should be relatively rare, as the async Task/ValueTask method builders
                // use the awaiter's UnsafeOnCompleted, so this will only happen with code that
                // explicitly uses the awaiter's OnCompleted instead.
                _executionContext = null;
                ExecutionContext.Run(ec, runState =>
                {
                    var t = (Tuple<Source, Action<object>, object>)runState;
                    t?.Item1.InvokeContinuation(t.Item2, t.Item3, forceAsync: false);
                }, Tuple.Create(this, previousContinuation, _state));
            }
        }

        public void GetResult(short token)
        {
        }

        public ValueTaskSourceStatus GetStatus(short token)
        {
            return _status;
        }

        public void OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            if ((flags & ValueTaskSourceOnCompletedFlags.FlowExecutionContext) != 0)
                _executionContext = ExecutionContext.Capture();

            if ((flags & ValueTaskSourceOnCompletedFlags.UseSchedulingContext) != 0)
            {
                var sc = SynchronizationContext.Current;
                if (sc != null && sc.GetType() != typeof(SynchronizationContext))
                    _scheduler = sc;
                else
                {
                    var ts = TaskScheduler.Current;
                    if (ts != TaskScheduler.Default)
                        _scheduler = ts;
                }
            }

            // Remember current state
            _state = state;
            // Remember continuation to be executed on completed (if not already completed, in case of which
            // continuation will be set to CallbackCompleted)
            var previousContinuation = Interlocked.CompareExchange(ref _continuation, continuation, null);
            if (previousContinuation != null)
            {
                if (!ReferenceEquals(previousContinuation, CallbackCompleted))
                    throw new InvalidOperationException();

                // Lost the race condition and the operation has now already completed.
                // We need to invoke the continuation, but it must be asynchronously to
                // avoid a stack dive.  However, since all of the queueing mechanisms flow
                // ExecutionContext, and since we're still in the same context where we
                // captured it, we can just ignore the one we captured.
                _executionContext = null;
                _state = null; // we have the state in "state"; no need for the one in UserToken
                InvokeContinuation(continuation, state, forceAsync: true);
            }

            _cb.Add(() => continuation(state));
        }

        private void InvokeContinuation(Action<object> continuation, object state, bool forceAsync)
        {
            if (continuation == null)
                return;

            var scheduler = _scheduler;
            _scheduler = null;
            if (scheduler != null)
            {
                if (scheduler is SynchronizationContext sc)
                {
                    sc.Post(s =>
                    {
                        var t = (Tuple<Action<object>, object>)s;
                        t?.Item1(t.Item2);
                    }, Tuple.Create(continuation, state));
                }
                else
                {
                    Debug.Assert(scheduler is TaskScheduler, $"Expected TaskScheduler, got {scheduler}");
                    Task.Factory.StartNew(continuation, state, CancellationToken.None, TaskCreationOptions.DenyChildAttach, (TaskScheduler)scheduler);
                }
            }
            else if (forceAsync)
                ThreadPool.QueueUserWorkItem(continuation, state, preferLocal: true);
            else
                continuation(state);
        }
    }

    public ValueTask WriteToBufferAsync(ReadOnlyMemory<byte> bytes)
    {
        lock (_sync)
        {
            if (Length + bytes.Length > _maxiumBufferSize && _maxiumBufferSize >= 0)
            {
                var source = new Source();
                Action ac = null;
                ac = () =>
                {
                    MemoryUnderLimit -= ac;
                    WriteToBufferNoCheck(bytes.Span);
                    source.Success();
                };
                MemoryUnderLimit += ac;
                return new ValueTask(source, 0);
            }
        }

        WriteToBufferNoCheck(bytes.Span);
        return default;
    }

    public void WriteToBuffer(ReadOnlySpan<byte> bytes)
    {
        while (Length + bytes.Length > _maxiumBufferSize && _maxiumBufferSize >= 0)
            Thread.Yield();
        WriteToBufferNoCheck(bytes);
    }

    private void TakeOutMemoryNoCheck(Span<byte> buffer)
    {
        lock (_sync)
        {
            var discardBuffers = new List<byte[]>();
            var prevDiscarded = false;
            if (Length < buffer.Length && _maxiumBufferSize >= 0)
                throw new InvalidProgramException();
            foreach (var b in _buffers)
            {
                if (buffer.Length == 0)
                    break;
                    
                var start = 0;
                var end = BufferSegmentSize;
                var isFirst = b == _buffers.First() || prevDiscarded;
                var isLast = b == _buffers.Last();
                    
                if (isFirst)
                    start = _bufferStart;
                    
                if (isLast)
                    end = _bufferEnd;
                    
                var length = end - start;
                var needToCopy = Math.Min(buffer.Length, length);
                b.AsSpan(start, needToCopy).CopyTo(buffer);
                start += needToCopy;
                    
                if (isFirst)
                    _bufferStart += needToCopy;

                if (end - start == 0)
                {
                    if (isFirst)
                        _bufferStart = 0;
                    if (isLast)
                        _bufferEnd = 0;
                    discardBuffers.Add(b);
                    prevDiscarded = true;
                }
                else
                    prevDiscarded = false;

                buffer = buffer[needToCopy..];
            }
                
            Debug.Assert(buffer.Length == 0 || _maxiumBufferSize < 0);
            while (discardBuffers.Any())
            {
                var b = discardBuffers.First();
                _arrayPool.Return(b);
                discardBuffers.Remove(b);
                _buffers.Remove(b);
            }
            if (!_buffers.Any())
                AddNewBufferSegment();
        }
        if (Length <= _maxiumBufferSize && _maxiumBufferSize >= 0)
            MemoryUnderLimit?.Invoke();
    }

    public ValueTask TakeOutMemoryAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        lock (_sync)
        {
            if (buffer.Length > Length && _maxiumBufferSize >= 0)
            {
                var source = new Source();
                var reg = ct.Register(() =>
                {
                    source.Cancel();
                });
                Action ac = null;
                ac = () =>
                {
                    if (buffer.Length > Length) return;
                    DataWritten -= ac;
                    reg.Dispose();
                    TakeOutMemoryNoCheck(buffer.Span);
                    source.Success();
                };
                DataWritten += ac;
                return new ValueTask(source, 0);
            }
        }

        TakeOutMemoryNoCheck(buffer.Span);
        return default;
    }

    public void TakeOutMemory(Span<byte> buffer)
    {
        while (buffer.Length > Length && _maxiumBufferSize >= 0)
            Thread.Yield();
        TakeOutMemoryNoCheck(buffer);
    }
        
    private bool _disposedValue;

    private void Dispose(bool disposing)
    {
        if (_disposedValue) return;
        if (disposing)
        {
            foreach (var buffer in _buffers)
                _arrayPool.Return(buffer);
            _buffers.Clear();
        }
        _disposedValue = true;
    }

    public void Dispose() => Dispose(true);
}