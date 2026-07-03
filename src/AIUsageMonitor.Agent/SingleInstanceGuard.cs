namespace AIUsageMonitor.Agent;
public sealed class SingleInstanceGuard : IDisposable
{
 private readonly Mutex mutex; private bool disposed;
 private SingleInstanceGuard(Mutex mutex) => this.mutex=mutex;
 public static SingleInstanceGuard? TryAcquire(string name){var mutex=new Mutex(true,name,out var owns);if(!owns){mutex.Dispose();return null;}return new(mutex);}
 public void Dispose(){if(disposed)return;disposed=true;mutex.ReleaseMutex();mutex.Dispose();}
}
