using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace TaskManager
{
    internal class TaskManager
    {
        private readonly BlockingCollection<Task> tasks = new BlockingCollection<Task>();

        private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        
        public event EventHandler OnTaskCreated;
        public event EventHandler OnTaskCompleted;
        public event EventHandler OnTaskCanceled;
        public event EventHandler<Exception> OnTaskGetError;

        private Task managementTask;

        private int startingTimeout = 10000;
        private int stoppingTimeout = 10000;

        private TaskManagerStatus status;

        public TaskManagerStatus Status
        {
            get { return status; }
            set { status = value; }
        }


        public void StartTaskManager()
        {
            this.Status = TaskManagerStatus.Starting;

            if (this.cancellationTokenSource == null)
            {
                this.cancellationTokenSource = new CancellationTokenSource();
            }

            this.managementTask = new Task(this.ManageTasks, this.cancellationTokenSource.Token, TaskCreationOptions.LongRunning);

            this.managementTask.Start(TaskScheduler.Default);

            Func<bool> startingDetector = (() => (!this.managementTask.IsCompleted && this.managementTask.Status == TaskStatus.Running));

            Task.Factory.StartNew(() => SpinWait.SpinUntil(startingDetector, this.startingTimeout));

            if (!startingDetector())
            {
                throw new TimeoutException($"Görev yöneticisi ({this.startingTimeout} ms) içerisinde başlatılamadı");
            }

            this.Status = TaskManagerStatus.Running;
        }

        public void StopTaskManager()
        {
            this.status = TaskManagerStatus.Stoping;

            this.cancellationTokenSource.Cancel();

            if (this.tasks.Count == 0)
            {
                this.CreateLoopStopTask();
            }

            Func<bool> stoppingDetector = (() => (this.managementTask == null || this.managementTask.IsCompleted));

            Task.Factory.StartNew(() => SpinWait.SpinUntil(stoppingDetector, this.stoppingTimeout));

            if (!stoppingDetector())
            {
                throw new TimeoutException($"Görev yöneticisi ({this.stoppingTimeout} ms) içerisinde durdurulamadı");
            }

            try
            {
                this.managementTask?.Dispose();
            }
            catch { }

            this.cancellationTokenSource?.Dispose();
            this.cancellationTokenSource = new CancellationTokenSource();

            this.status = TaskManagerStatus.Stopped;
        }

        private Task CreateLoopStopTask()
        {
            Task task = new Task(() => 
            { 
                // Do nothing
            },
            this.cancellationTokenSource.Token);

            this.AddTaskToTaskList(task);

            return task;
        }

        private Task CreateTask(Action methodToBeProcessed)
        {
            Task task = new Task(methodToBeProcessed, this.cancellationTokenSource.Token);

            this.AddTaskToTaskList(task);

            return task;
        }

        private Task<T> CreateTask<T>(Func<T> methodToBeProcessed)
        {
            Task<T> task = new Task<T>(methodToBeProcessed, this.cancellationTokenSource.Token);

            this.AddTaskToTaskList(task);

            return task;
        }

        private void AddTaskToTaskList(Task task)
        {
            this.tasks.Add(task);

            if (!(task is LoopStopTask))
            {
                this.OnTaskCreated?.Invoke(task, EventArgs.Empty);
            }
        }

        private void ManageTasks()
        {
            while (true)
            {
                Task task = this.tasks.Take();

                if (!(task is LoopStopTask))
                {
                    if (!task.IsCompleted)
                    {
                        task.Start();

                        if (!task.IsCompleted)
                        {
                            try
                            {
                                task.Wait();
                            }
                            catch (Exception e)
                            {
                                this.OnTaskGetError?.Invoke(task, e);
                                continue;
                            }

                            this.OnTaskCompleted?.Invoke(task, EventArgs.Empty);
                        }
                    }
                    else
                    {
                        this.OnTaskCanceled?.Invoke(task, EventArgs.Empty);
                    }
                }

                try
                {
                    task?.Dispose();
                }
                catch { }

                if (this.cancellationTokenSource.Token.IsCancellationRequested)
                {
                    break;
                }
            }

            while (this.tasks.Count > 0)
            {
                Task pendingTask;

                this.tasks.TryTake(out pendingTask);

                if (!(pendingTask is LoopStopTask))
                {

                }
            }
        }
    }

    public class LoopStopTask : Task
    {
        public LoopStopTask(Action action, CancellationToken cancellationToken) : base(action, cancellationToken)
        {
        }
    }

    public enum TaskManagerStatus
    {
        NotStarted = 0,
        Starting   = 1,
        Running    = 2,
        Stoping    = 3,
        Stopped    = 4
    }
}
