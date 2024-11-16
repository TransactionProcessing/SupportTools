namespace TransactionProcessing.SchedulerService.DataGenerator;

public class TraceEventArgs : EventArgs
{
    public enum Level
    {
        Trace,
        Warning,
        Error
    }

    #region Properties

    public String Message { get; set; }

    public Level TraceLevel { get; set; }

    #endregion
}