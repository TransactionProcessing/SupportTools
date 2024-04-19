﻿namespace TransactionProcessing.DataGeneration;

using System.Text;

public abstract class UploadFile{
    #region Fields

    protected Int32 FileLineCount;

    protected List<String> FileLines;

    protected Decimal TotalValue;

    #endregion

    #region Constructors

    protected UploadFile(Guid estateId, Guid merchantId, Guid contractId, Guid productId, Guid fileProfileId, Guid userId)
    {
        this.UserId = userId;
        this.FileLines = new List<String>();
        this.EstateId = estateId;
        this.MerchantId = merchantId;
        this.ContractId = contractId;
        this.ProductId = productId;
        this.FileProfileId = fileProfileId;
    }

    #endregion

    #region Properties

    public Guid EstateId{ get; protected set; }
    public Guid ContractId { get; protected set; }
    public Guid ProductId { get; protected set; }
    public Guid FileProfileId{ get; protected set; }
    public Guid MerchantId{ get; protected set; }
    public Decimal TotalAmount { get; protected set; }
    public Guid UserId { get; protected set; }

    #endregion

    #region Methods

    public void AddHeader(DateTime dateTime){
        this.FileLines.Add($"H,{dateTime:yyyy-MM-dd-HH-mm-ss}");
    }

    public void AddTrailer(){
        this.FileLines.Add($"T,{this.FileLineCount}");
    }

    public Byte[] GetFileContents(){
        StringBuilder fileData = new StringBuilder();
        String? header = this.FileLines.SingleOrDefault(f => f.StartsWith("H"));
        fileData.Append(header);

        List<String> detailLines = this.FileLines.Where(f => f.StartsWith("D")).ToList();
        foreach (String detailLine in detailLines){
            fileData.AppendLine(detailLine);
        }
        String? trailer = this.FileLines.SingleOrDefault(f => f.StartsWith("T"));
        fileData.Append(trailer);

        return Encoding.UTF8.GetBytes(fileData.ToString());
    }

    public Int32 GetNumberOfLines(){
        return this.FileLines.Count;
    }

    #endregion
}