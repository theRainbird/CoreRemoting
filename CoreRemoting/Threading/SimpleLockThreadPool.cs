using System;
using System.Security.Permissions;
using System.Threading;
using CoreRemoting.Toolbox;

namespace CoreRemoting.Threading;

/// <summary>
/// Thread pool with simple locking work item queue.
/// </summary>
/// <remarks>
/// Written by Joe Duffy as a part of the �Building a custom thread pool� series:
/// http://www.bluebytesoftware.com/blog/2008/07/29/BuildingACustomThreadPoolSeriesPart1.aspx
/// https://joeduffyblog.com/2008/07/29/building-a-custom-thread-pool-series-part-1/
/// </remarks>
public sealed class SimpleLockThreadPool : IThreadPool
{
    // Constructors--
    // Two things may be specified:
    //   ConcurrencyLevel == fixed # of threads to use
    //   FlowExecutionContext == whether to capture & flow ExecutionContexts for work items

    /// <summary>
    /// Initializes a new instance of the <see cref="SimpleLockThreadPool" /> class.
    /// </summary>
    public SimpleLockThreadPool() :
        this(Environment.ProcessorCount, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SimpleLockThreadPool" /> class.
    /// </summary>
    /// <param name="concurrencyLevel">The concurrency level.</param>
    public SimpleLockThreadPool(int concurrencyLevel) :
        this(concurrencyLevel, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SimpleLockThreadPool" /> class.
    /// </summary>
    /// <param name="concurrencyLevel">The concurrency level.</param>
    /// <param name="limit">High watermark limit for the work item queue size.</param>
    public SimpleLockThreadPool(int concurrencyLevel, int limit) :
        this(concurrencyLevel, true, limit)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SimpleLockThreadPool" /> class.
    /// </summary>
    /// <param name="flowExecutionContext">if set to <c>true</c> the execution context is flown.</param>
    public SimpleLockThreadPool(bool flowExecutionContext) :
        this(Environment.ProcessorCount, flowExecutionContext)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SimpleLockThreadPool" /> class.
    /// </summary>
    /// <param name="concurrencyLevel">The concurrency level.</param>
    /// <param name="flowExecutionContext">if set to <c>true</c>, the execution context is flown.</param>
    /// <param name="limit">High watermark limit for the work item queue size.</param>
    public SimpleLockThreadPool(int concurrencyLevel, bool flowExecutionContext, int limit = 0)
    {
        if (concurrencyLevel <= 0)
            throw new ArgumentOutOfRangeException("concurrencyLevel");

        m_concurrencyLevel = concurrencyLevel;
        m_flowExecutionContext = flowExecutionContext;
        m_queue = new LimitedSizeQueue<WorkItem>(limit);

#if !XAMARIN
        // If suppressing flow, we need to demand permissions.
        if (!flowExecutionContext)
            new SecurityPermission(SecurityPermissionFlag.Infrastructure).Demand();
#endif
    }

    /// <summary>
    /// Each work item consists of a closure: work + (optional) state obj + context.
    /// </summary>
    private struct WorkItem
    {
        internal WaitCallback m_work;
        internal object m_obj;
        internal ExecutionContext m_executionContext;

        internal WorkItem(WaitCallback work, object obj)
        {
            m_work = work;
            m_obj = obj;
            m_executionContext = null;
        }

        internal void Invoke()
        {
            // Run normally (delegate invoke) or under context, as appropriate.
            if (m_executionContext == null)
                m_work(m_obj);
            else
                ExecutionContext.Run(m_executionContext, ContextInvoke, null);
        }

        private void ContextInvoke(object obj)
        {
            m_work(m_obj);
        }
    }

    /// <summary>
    /// Gets or sets the worker thread names.
    /// </summary>
    /// <remarks>
    /// Setting this property has no effect if worked threads are already started.
    /// </remarks>
    public string WorkerThreadName { get; set; }

    private readonly int m_concurrencyLevel;
    private readonly bool m_flowExecutionContext;
    private readonly LimitedSizeQueue<WorkItem> m_queue;
    private volatile Thread[] m_threads;
    private int m_threadsWaiting;
    private bool m_shutdown;

    // Methods to queue work.

    /// <summary>
    /// Queues a method for the execution, and specifies an object to be used by the method.
    /// </summary>
    /// <param name="work">A <see cref="WaitCallback" /> representing the method to execute.</param>
    /// <param name="obj">An object containing data to be used by the method.</param>
    public void QueueUserWorkItem(WaitCallback work, object obj)
    {
        WorkItem wi = new WorkItem(work, obj);

        // If execution context flowing is on, capture the caller's context.
        if (m_flowExecutionContext)
            wi.m_executionContext = ExecutionContext.Capture();

        // Make sure the pool is started (threads created, etc).
        EnsureStarted();

        // Now insert the work item into the queue, possibly waking a thread.
        lock (m_queue)
        {
            m_queue.TryEnqueue(wi);
            if (m_threadsWaiting > 0)
                Monitor.Pulse(m_queue);
        }
    }

    // Ensures that threads have begun executing.

    private void EnsureStarted()
    {
        if (m_threads == null)
        {
            lock (m_queue)
            {
                if (m_threads == null)
                {
                    m_threads = new Thread[m_concurrencyLevel];
                    for (int i = 0; i < m_threads.Length; i++)
                    {
                        m_threads[i] = new Thread(DispatchLoop);
                        m_threads[i].IsBackground = true;

                        // annotate worker threads
                        if (!string.IsNullOrEmpty(WorkerThreadName))
                        {
                            m_threads[i].Name = WorkerThreadName;
                        }

                        m_threads[i].Start();
                    }
                }
            }
        }
    }

    // Each thread runs the dispatch loop.

    private void DispatchLoop()
    {
        while (true)
        {
            var wi = default(WorkItem);

            lock (m_queue)
            {
                // If shutdown was requested, exit the thread.
                if (m_shutdown)
                    return;

                // Find a new work item to execute.
                while (m_queue.TryDequeue(out wi) == false)
                {
                    m_threadsWaiting++;
                    try { Monitor.Wait(m_queue); }
                    finally { m_threadsWaiting--; }

                    // If we were signaled due to shutdown, exit the thread.
                    if (m_shutdown)
                        return;
                }

                // We found a work item! Grab it ...
            }

            // ...and Invoke it. Note: exceptions will go unhandled (and crash).
            wi.Invoke();
        }
    }

    // Disposing will signal shutdown, and then wait for all threads to finish.

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    public void Dispose()
    {
        Stop();

        if (m_threads != null)
        {
            for (int i = 0; i < m_threads.Length; i++)
                m_threads[i].Join();
        }
    }

    /// <summary>
    /// Stops dispatching the work items and clears the work item queue.
    /// Doesn't wait for the work threads to stop.
    /// </summary>
    public void Stop()
    {
        m_shutdown = true;
        lock (m_queue)
        {
            Monitor.PulseAll(m_queue);
        }

        m_queue.Clear();
    }
}