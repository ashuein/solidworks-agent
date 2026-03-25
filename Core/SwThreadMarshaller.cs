using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClaudeSW.Core
{
    /// <summary>
    /// Marshals actions to the SolidWorks STA thread. SolidWorks COM objects
    /// are apartment-threaded and must be called from the thread that created them.
    /// Uses a hidden WinForms control for Invoke().
    /// </summary>
    public class SwThreadMarshaller : IDisposable
    {
        private readonly Control _control;

        public SwThreadMarshaller()
        {
            // Create a hidden control on the current (STA) thread
            _control = new Control();
            _control.CreateControl(); // Force handle creation
        }

        /// <summary>
        /// Executes the action on the STA thread synchronously.
        /// Safe to call from any thread.
        /// </summary>
        public Task InvokeAsync(Action action)
        {
            if (_control.InvokeRequired)
            {
                var tcs = new TaskCompletionSource<bool>();
                _control.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        action();
                        tcs.SetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                }));
                return tcs.Task;
            }
            else
            {
                action();
                return Task.FromResult(true);
            }
        }

        public void Dispose()
        {
            if (!_control.IsDisposed)
                _control.Dispose();
        }
    }
}
