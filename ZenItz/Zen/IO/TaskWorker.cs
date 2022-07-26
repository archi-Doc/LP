﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable SA1401 // Fields should be private
#pragma warning disable SA1307 // Accessible fields should begin with upper-case letter

namespace ZenItz;

/// <summary>
/// Represents a interface for processing <typeparamref name="TWork"/>.
/// </summary>
/// <typeparam name="TWork">The type of the work.</typeparam>
public sealed class TaskWorkInterface<TWork>
    where TWork : notnull
{
    public TaskWorkInterface(TaskWorker<TWork> taskWorker, TWork work)
    {
        this.TaskWorker = taskWorker;
        this.Work = work;
        if (taskWorker.NumberOfConcurrentTasks == 1)
        {
            this.taskOrTcs = new TaskCompletionSource();
        }
        else
        {
            this.taskOrTcs = new Task(() =>
            {
                try
                {
                    this.TaskWorker.method(this.TaskWorker, this.Work).Wait();
                }
                finally
                {
                    this.TaskWorker.FinishWork(this);
                }
            });
        }
    }

    /// <summary>
    /// Wait until the work is completed.
    /// </summary>
    /// <returns><see langword="true"/>: The work is complete<br/><see langword="false"/>: Not complete.</returns>
    public Task<bool> WaitForCompletionAsync() => this.WaitForCompletionAsync(TimeSpan.MinValue);

    /// <summary>
    /// Wait for the specified time until the work is completed.
    /// </summary>
    /// <param name="millisecondsToWait">The number of milliseconds to wait, or -1 to wait indefinitely.</param>
    /// <returns><see langword="true"/>: The work is complete<br/><see langword="false"/>: Not complete.</returns>
    public Task<bool> WaitForCompletionAsync(int millisecondsToWait) => this.WaitForCompletionAsync(TimeSpan.FromMilliseconds(millisecondsToWait));

    /// <summary>
    /// Wait for the specified time until the work is completed.
    /// </summary>
    /// <param name="timeToWait">The TimeSpan to wait, or negative value (e.g TimeSpan.MinValue) to wait indefinitely.</param>
    /// <returns><see langword="true"/>: The work is complete<br/><see langword="false"/>: Not complete.</returns>
    public async Task<bool> WaitForCompletionAsync(TimeSpan timeToWait)
    {
        var state = this.State;
        if (state == TaskWorkState.Complete)
        {// Complete
            return true;
        }
        else if (state == TaskWorkState.Aborted)
        {// Aborted
            return false;
        }
        else if (this.TaskWorker.IsTerminated)
        {// Terminated
            return false;
        }

        // Standby or Working
        try
        {
            if (timeToWait < TimeSpan.Zero)
            {
                if (this.taskOrTcs is TaskCompletionSource tcs)
                {
                    await tcs.Task.WaitAsync(this.TaskWorker.CancellationToken).ConfigureAwait(false);
                }
                else if (this.taskOrTcs is Task task)
                {
                    await task.WaitAsync(this.TaskWorker.CancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                if (this.taskOrTcs is TaskCompletionSource tcs)
                {
                    await tcs.Task.WaitAsync(timeToWait, this.TaskWorker.CancellationToken).ConfigureAwait(false);
                }
                else if (this.taskOrTcs is Task task)
                {
                    await task.WaitAsync(timeToWait, this.TaskWorker.CancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (TimeoutException)
        {// Timeout
            return false;
        }
        catch
        {// Cancellation
            return false;
        }

        if (this.taskOrTcs is TaskCompletionSource tcs2 &&
            tcs2.Task.Status == TaskStatus.RanToCompletion)
        {// Complete
            return true;
        }
        else if (this.taskOrTcs is Task task2 &&
            task2.Status == TaskStatus.RanToCompletion)
        {// Complete
            return true;
        }
        else
        {// Standby or Working or Aborted
            return false;
        }
    }

    /// <summary>
    /// Gets an instance of <see cref="TaskWorker{TWork}"/>.
    /// </summary>
    public TaskWorker<TWork> TaskWorker { get; }

    /// <summary>
    /// Gets an instance of <typeparamref name="TWork"/>.
    /// </summary>
    public TWork Work { get; }

    /// <summary>
    /// Gets a state of the work (Standby -> Working -> Complete or Aborted).
    /// </summary>
    public TaskWorkState State
    {
        // Created: node:null, task:Task.New
        // Standby: node:standby, task:Task.New
        // Working: node:working, task:Task.New
        // Complete/Abort(Cancelled): node:null, task:Task.New
        get
        {
            var list = this.node?.List;
            if (list == this.TaskWorker.StandbyList)
            {// Standby
                return TaskWorkState.Standby;
            }
            else if (list == this.TaskWorker.WorkingList)
            {// Working
                return TaskWorkState.Working;
            }

            var status = TaskStatus.Created;
            if (this.taskOrTcs is TaskCompletionSource tcs)
            {
                status = tcs.Task.Status;
            }
            else if (this.taskOrTcs is Task task)
            {
                status = task.Status;
            }

            if (status == TaskStatus.RanToCompletion)
            {// Complete
                return TaskWorkState.Complete;
            }
            else if (status == TaskStatus.Canceled || status == TaskStatus.Faulted)
            {// TaskWorkState
                return TaskWorkState.Aborted;
            }

            return TaskWorkState.Created;
        }
    }

    public override string ToString() => $"State: {this.State}, Work: {this.Work}";

    internal LinkedListNode<TaskWorkInterface<TWork>>? node; // null: , not null: standby list or working list
    // internal TaskCompletionSource? tcs;
    internal object taskOrTcs;
}

/// <summary>
/// Represents a worker class.<br/>
/// <see cref="TaskWorker{TWork}"/> uses <see cref="HashSet{TWork}"/> and <see cref="LinkedList{TWork}"/> to manage works.
/// </summary>
/// <typeparam name="TWork">The type of the work.</typeparam>
public class TaskWorker<TWork> : TaskCore
    where TWork : notnull
{
    /// <summary>
    /// Defines the type of delegate to process a work.
    /// </summary>
    /// <param name="worker">A worker instance.</param>
    /// <param name="work">A work instance.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public delegate Task WorkDelegate(TaskWorker<TWork> worker, TWork work);

    /*private static Action<Task> trySetResult;

    static TaskWorker()
    {
        var method = typeof(Task).GetMethod("TrySetResult", BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic) !;
        var arg = Expression.Parameter(typeof(Task));
        trySetResult = Expression.Lambda<Action<Task>>(Expression.Call(arg, method), arg).Compile();
    }*/

    private static async Task Process(object? parameter)
    {
        var worker = (TaskWorker<TWork>)parameter!;
        while (!worker.IsTerminated)
        {
            var updateEvent = worker.updateEvent;
            if (updateEvent == null)
            {
                break;
            }

            try
            {
                await updateEvent.WaitAsync(worker.CancellationToken).ConfigureAwait(false); // Add or Finish
            }
            catch
            {
                break;
            }

            while (true)
            {
                TaskWorkInterface<TWork>? workInterface;
                lock (worker.syncObject)
                {
                    workInterface = worker.standbyList.FirstOrDefault();
                    if (workInterface == null)
                    {// No work left.
                        break;
                    }
                    else if (worker.NumberOfConcurrentTasks > 0 && worker.workingList.Count >= worker.NumberOfConcurrentTasks)
                    {// The maximum number of concurrent tasks reached.
                        break;
                    }

                    worker.standbyList.Remove(workInterface.node!); // Standby list -> Working list
                    workInterface.node = worker.workingList.AddLast(workInterface);
                }

                if (workInterface.taskOrTcs is TaskCompletionSource tcs)
                {// Executes a work on this task.
                    try
                    {
                        await worker.method(worker, workInterface.Work).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                    finally
                    {
                        worker.FinishWork(workInterface); // trySetResult(workInterface.task);
                    }
                }
                else if (workInterface.taskOrTcs is Task task)
                {// Start a new task for each work.
                    task.Start();
                }
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskWorker{T}"/> class.<br/>
    /// </summary>
    /// <param name="parent">The parent.</param>
    /// <param name="method">The method that receives and processes a work.</param>
    /// <param name="startImmediately">Starts the worker immediately.<br/>
    /// <see langword="false"/>: Manually call <see cref="ThreadCore.Start" /> to start the worker.</param>
    public TaskWorker(ThreadCoreBase parent, WorkDelegate method, bool startImmediately = true)
        : base(parent, Process, startImmediately)
    {
        this.method = method;
        if (startImmediately)
        {
            this.Start();
        }
    }

    /// <summary>
    /// Add a work at the start of the work queue.
    /// </summary>
    /// <param name="work">A work to be added.</param>
    /// <returns><see langword="true"/>: Success, <see langword="false"/>: The work already exists.</returns>
    public TaskWorkInterface<TWork> AddFirst(TWork work)
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(null);
        }

        TaskWorkInterface<TWork>? workInterface;
        lock (this.syncObject)
        {
            if (this.workToInterface.TryGetValue(work, out workInterface))
            {
                return workInterface;
            }

            workInterface = new(this, work);
            this.workToInterface.Add(work, workInterface);
            workInterface.node = this.standbyList.AddFirst(workInterface);
        }

        this.updateEvent?.Pulse();
        return workInterface;
    }

    /// <summary>
    /// Add a work at the end of the work queue.
    /// </summary>
    /// <param name="work">A work to be added..</param>
    /// <returns><see langword="true"/>: Success, <see langword="false"/>: The work already exists.</returns>
    public TaskWorkInterface<TWork> AddLast(TWork work)
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(null);
        }

        TaskWorkInterface<TWork>? workInterface;
        lock (this.syncObject)
        {
            if (this.workToInterface.TryGetValue(work, out workInterface))
            {
                return workInterface;
            }

            workInterface = new(this, work);
            this.workToInterface.Add(work, workInterface);
            workInterface.node = this.standbyList.AddLast(workInterface);
        }

        this.updateEvent?.Pulse();
        return workInterface;
    }

    /// <summary>
    /// Waits for the completion of all works.
    /// </summary>
    /// <returns><see langword="true"/>: All works are complete.<br/><see langword="false"/>: Timeout or cancelled.</returns>
    public Task<bool> WaitForCompletionAsync() => this.WaitForCompletionAsync(TimeSpan.MinValue);

    /// <summary>
    /// Waits for the completion of all works.
    /// </summary>
    /// <param name="millisecondsToWait">The number of milliseconds to wait, or -1 to wait indefinitely.</param>
    /// <returns><see langword="true"/>: All works are complete.<br/><see langword="false"/>: Timeout or cancelled.</returns>
    public Task<bool> WaitForCompletionAsync(int millisecondsToWait) => this.WaitForCompletionAsync(TimeSpan.FromMilliseconds(millisecondsToWait));

    /// <summary>
    /// Waits for the completion of all works.
    /// </summary>
    /// /// <param name="timeToWait">The TimeSpan to wait, or negative value (e.g TimeSpan.MinValue) to wait indefinitely.</param>
    /// <returns><see langword="true"/>: All works are complete.<br/><see langword="false"/>: Timeout or cancelled.</returns>
    public async Task<bool> WaitForCompletionAsync(TimeSpan timeToWait)
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(null);
        }

        TimeSpan elapsed = TimeSpan.Zero;
        while (!this.IsTerminated)
        {
            object? taskOrTcs;
            lock (this.syncObject)
            {// Get a standby or working task.
                taskOrTcs = this.standbyList.LastOrDefault()?.taskOrTcs ?? this.workingList.LastOrDefault()?.taskOrTcs;
                if (taskOrTcs == null)
                {// No task (complete)
                    return true;
                }
            }

            if (elapsed != TimeSpan.Zero)
            {// After WaitAsync()
                if (timeToWait < TimeSpan.Zero)
                {// Wait indefinitely
                }
                else if (timeToWait <= elapsed)
                {// Timeout
                    return false;
                }
                else
                {
                    timeToWait -= elapsed;
                }
            }

            this.stopwatch.Restart();
            try
            {
                if (timeToWait < TimeSpan.Zero)
                {
                    if (taskOrTcs is TaskCompletionSource tcs)
                    {
                        await tcs.Task.WaitAsync(this.CancellationToken).ConfigureAwait(false);
                    }
                    else if (taskOrTcs is Task task)
                    {
                        await task.WaitAsync(this.CancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    if (taskOrTcs is TaskCompletionSource tcs)
                    {
                        await tcs.Task.WaitAsync(timeToWait, this.CancellationToken).ConfigureAwait(false);
                    }
                    else if (taskOrTcs is Task task)
                    {
                        await task.WaitAsync(timeToWait, this.CancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch
            {// Timeout or cancelled
                return false;
            }

            elapsed = this.stopwatch.Elapsed;
        }

        return false;
    }

    /// <summary>
    /// Gets or sets the maximum number of concurrent tasks.<br/>
    /// The default is 1.<br/>
    /// 0 or less is unlimited.
    /// </summary>
    public int NumberOfConcurrentTasks { get; set; } = 1;

    /// <summary>
    /// Gets the number of works in the standby queue.
    /// </summary>
    public int StandbyCount => this.standbyList.Count;

    /// <summary>
    /// Gets the number of works in the working queue.
    /// </summary>
    public int WorkingCount => this.workingList.Count;

    internal AsyncPulseEvent? updateEvent = new();

    internal void FinishWork(TaskWorkInterface<TWork> workInterface)
    {
        lock (this.syncObject)
        {
            this.workToInterface.Remove(workInterface.Work);
            var node = workInterface.node;
            node?.List?.Remove(node);
            workInterface.node = null; // Complete or Aborted
        }

        Console.WriteLine("trySetResult");
        if (workInterface.taskOrTcs is TaskCompletionSource tcs)
        {
            // Task.Run(() => tcs.SetResult());
            tcs.SetResult();
        }
        else
        {
            this.updateEvent?.Pulse();
        }

        Console.WriteLine("trySetResult end");
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (!this.disposed)
        {
            if (disposing)
            {
                this.updateEvent = null;
            }

            base.Dispose(disposing);
        }
    }

    internal LinkedList<TaskWorkInterface<TWork>> StandbyList => this.standbyList;

    internal LinkedList<TaskWorkInterface<TWork>> WorkingList => this.workingList;

    internal WorkDelegate method;
    private object syncObject = new();
    private Dictionary<TWork, TaskWorkInterface<TWork>> workToInterface = new();
    private LinkedList<TaskWorkInterface<TWork>> standbyList = new();
    private LinkedList<TaskWorkInterface<TWork>> workingList = new();
    private Stopwatch stopwatch = new();
}
