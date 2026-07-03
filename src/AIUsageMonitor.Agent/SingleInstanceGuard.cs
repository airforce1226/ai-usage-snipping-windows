namespace AIUsageMonitor.Agent;
public sealed class SingleInstanceGuard : IDisposable
{
 private readonly ManualResetEventSlim release; private readonly Thread ownerThread; private int disposed;
 private SingleInstanceGuard(ManualResetEventSlim release,Thread ownerThread){this.release=release;this.ownerThread=ownerThread;}
 public static SingleInstanceGuard? TryAcquire(string name)
 {
  ArgumentException.ThrowIfNullOrWhiteSpace(name);
  var ready=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  var release=new ManualResetEventSlim();
  var thread=new Thread(()=>{using var mutex=new Mutex(true,name,out var owns);ready.SetResult(owns);if(!owns)return;release.Wait();mutex.ReleaseMutex();}){IsBackground=true,Name="AIUsageMonitor mutex owner"};
  thread.Start();
  if(!ready.Task.GetAwaiter().GetResult()){release.Dispose();thread.Join();return null;}
  return new(release,thread);
 }
 public void Dispose(){if(Interlocked.Exchange(ref disposed,1)!=0)return;release.Set();ownerThread.Join();release.Dispose();}
}
