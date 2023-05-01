namespace TransactionProcessing.DataGeneration;

public class MobileTopupUploadFile : UploadFile{
    #region Constructors

    public MobileTopupUploadFile(Guid estateId, Guid merchantId, Guid fileProfileId, Guid userId) : base(estateId, merchantId, fileProfileId,userId){
    }

    #endregion

    #region Methods

    public void AddLine(Decimal amount, String mobileNumber){
        this.TotalValue += amount;
        this.FileLines.Add($"D,{mobileNumber},{amount}");
        this.FileLineCount++;
    }

    #endregion
}