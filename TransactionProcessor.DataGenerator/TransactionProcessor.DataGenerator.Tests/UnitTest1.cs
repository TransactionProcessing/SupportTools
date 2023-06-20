using TransactionProcessing.DataGeneration;

namespace TransactionProcessor.DataGenerator.Tests
{
    using Shouldly;

    public class UnitTest1
    {
        [Fact]
        public void TransactionDataGenerator_GetTransactionDateTime_DateOnly_TimeIsGenerated(){
            DateTime dateTime = new DateTime(2023, 05, 16);
            Random r =  new Random();
            DateTime result = TransactionDataGenerator.GetTransactionDateTime(r, dateTime);
            result.Year.ShouldBe(dateTime.Year);
            result.Month.ShouldBe(dateTime.Month);
            result.Day.ShouldBe(dateTime.Day);
            result.Hour.ShouldNotBe(dateTime.Hour);
            result.Minute.ShouldNotBe(dateTime.Minute);
            result.Second.ShouldNotBe(dateTime.Second);
        }

        [Fact]
        public void TransactionDataGenerator_GetTransactionDateTime_DateWithHoursAndMinutes_OnlySecondsAreGenerated()
        {
            DateTime dateTime = new DateTime(2023, 05, 16, 9,30,0);
            Random r = new Random();
            DateTime result = TransactionDataGenerator.GetTransactionDateTime(r, dateTime);
            result.Year.ShouldBe(dateTime.Year);
            result.Month.ShouldBe(dateTime.Month);
            result.Day.ShouldBe(dateTime.Day);
            result.Hour.ShouldBe(dateTime.Hour);
            result.Minute.ShouldBe(dateTime.Minute);
            result.Second.ShouldNotBe(dateTime.Second);
        }

        [Fact]
        public void TransactionDataGenerator_GetTransactionDateTime_DateWithHours_OnlySecondsAreGenerated()
        {
            DateTime dateTime = new DateTime(2023, 05, 16, 9, 0, 0);
            Random r = new Random();
            DateTime result = TransactionDataGenerator.GetTransactionDateTime(r, dateTime);
            result.Year.ShouldBe(dateTime.Year);
            result.Month.ShouldBe(dateTime.Month);
            result.Day.ShouldBe(dateTime.Day);
            result.Hour.ShouldBe(dateTime.Hour);
            result.Minute.ShouldBe(dateTime.Minute);
            result.Second.ShouldNotBe(dateTime.Second);
        }
    }
}