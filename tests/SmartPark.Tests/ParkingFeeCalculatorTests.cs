using SmartPark.Core.Models;
using SmartPark.Core.Services;
using FsCheck;
using FsCheck.Xunit;

namespace SmartPark.Tests;

public class ParkingFeeCalculatorTests
{
    private readonly ParkingFeeCalculator _calculator = new();
    private static readonly DateTime Monday10Am = new(2026, 3, 16, 10, 0, 0);

    #region Edge Cases

    [Fact]
    public void CalculateFee_CheckOutBeforeCheckIn_ThrowsArgumentException()
    {
        // Arrange
        var checkIn  = new DateTime(2026, 3, 16, 12, 0, 0);
        var checkOut = new DateTime(2026, 3, 16, 10, 0, 0);

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut));
    }

    [Fact]
    public void CalculateFee_ZeroDuration_ReturnsFree()
    {
        // Arrange
        var checkIn  = Monday10Am;
        var checkOut = checkIn;

        // Act
        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(0m, result.TotalFee);
    }

    #endregion

    #region Grace Period

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(30)]
    public void CalculateFee_WithinGracePeriod_ReturnsFree(int minutes)
    {
        // Arrange
        var checkIn  = Monday10Am;
        var checkOut = checkIn.AddMinutes(minutes);

        // Act
        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(0m, result.BaseFee);
        Assert.Equal(0m, result.TotalFee);
    }

    [Fact]
    public void CalculateFee_31Minutes_ChargesOneHour()
    {
        // Arrange
        var checkIn  = Monday10Am;
        var checkOut = checkIn.AddMinutes(31);

        // Act
        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(1_000m, result.TotalFee);
    }

    #endregion

    #region Basic Fee Calculation

    [Theory]
    [InlineData(VehicleType.Motorcycle, 2,  1_000)]
    [InlineData(VehicleType.Car,        3,  3_000)]
    [InlineData(VehicleType.SUV,        1,  1_500)]
    public void CalculateFee_BasicRate_CorrectFee(VehicleType type, int hours, decimal expected)
    {
        // Arrange
        var checkIn  = Monday10Am;
        var checkOut = checkIn.AddHours(hours);

        // Act
        var result = _calculator.CalculateFee(type, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(expected, result.TotalFee);
    }

    #endregion

    #region Duration Rounding

    [Theory]
    [InlineData(91,  2)]
    [InlineData(90,  1)]
    public void CalculateFee_PartialHours_RoundsUp(int totalMinutes, int expectedHours)
    {
        // Arrange
        var checkIn  = Monday10Am;
        var checkOut = checkIn.AddMinutes(totalMinutes);

        // Act
        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(expectedHours * 1_000m, result.BaseFee);
    }

    #endregion

    #region Daily Cap

    [Theory]
    [InlineData(VehicleType.Motorcycle, 10,  4_000)]
    [InlineData(VehicleType.Car,        12,  8_000)]
    [InlineData(VehicleType.SUV,        24, 12_000)]
    public void CalculateFee_ExceedsDailyCap_CapsAtMaximum(VehicleType type, int hours, decimal expected)
    {
        // Arrange
        var checkIn  = Monday10Am;
        var checkOut = checkIn.AddHours(hours);

        // Act
        var result = _calculator.CalculateFee(type, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(expected, result.BaseFee);
    }

    #endregion

    #region Overnight Fee

    [Fact]
    public void CalculateFee_SessionPast10Pm_AddsOvernightFee()
    {
        // Arrange
        var checkIn  = new DateTime(2026, 3, 16, 20, 0, 0);
        var checkOut = new DateTime(2026, 3, 16, 23, 0, 0);

        // Act
        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(5_000m, result.TotalFee);
    }

    [Fact]
    public void CalculateFee_SessionBefore10Pm_NoOvernightFee()
    {
        // Arrange
        var checkIn  = new DateTime(2026, 3, 16, 8,  0, 0);
        var checkOut = new DateTime(2026, 3, 16, 17, 0, 0);

        // Act
        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(result.BaseFee, result.TotalFee);
    }

    #endregion

    #region Weekend Surcharge

    [Theory]
    [InlineData(2026, 3, 21, 2_400)] // Saturday
    [InlineData(2026, 3, 22, 2_400)] // Sunday
    [InlineData(2026, 3, 16, 2_000)] // Monday
    public void CalculateFee_WeekendSurcharge_AppliesCorrectly(
        int year, int month, int day, decimal expected)
    {
        // Arrange
        var checkIn  = new DateTime(year, month, day, 10, 0, 0);
        var checkOut = checkIn.AddHours(2);

        // Act
        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(expected, result.TotalFee);
    }

    #endregion

    #region Holiday Surcharge

    [Fact]
    public void CalculateFee_Holiday_Applies50PercentSurcharge()
    {
        // Arrange
        var checkIn  = Monday10Am;
        var checkOut = checkIn.AddHours(2);

        // Act
        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut, isHoliday: true);

        // Assert
        Assert.Equal(3_000m, result.TotalFee);
    }

    [Fact]
    public void CalculateFee_HolidayOnWeekend_OnlyHolidaySurcharge()
    {
        // Arrange
        var checkIn  = new DateTime(2026, 3, 21, 10, 0, 0); // Saturday
        var checkOut = checkIn.AddHours(2);

        // Act
        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut, isHoliday: true);

        // Assert
        Assert.Equal(3_000m, result.TotalFee);
    }

    #endregion

    #region Membership Discounts

    [Theory]
    [InlineData(MembershipTier.Guest,    2_000)]
    [InlineData(MembershipTier.Silver,   1_800)]
    [InlineData(MembershipTier.Gold,     1_500)]
    [InlineData(MembershipTier.Platinum, 1_200)]
    public void CalculateFee_MembershipDiscount_AppliesCorrectly(MembershipTier tier, decimal expected)
    {
        // Arrange
        var checkIn  = Monday10Am;
        var checkOut = checkIn.AddHours(2);

        // Act
        var result = _calculator.CalculateFee(VehicleType.Car, tier, checkIn, checkOut);

        // Assert
        Assert.Equal(expected, result.TotalFee);
    }

    #endregion

    #region Lost Ticket

    [Fact]
    public void CalculateFee_LostTicket_AddsFixedPenalty()
    {
        // Arrange
        var checkIn  = Monday10Am;
        var checkOut = checkIn.AddHours(2);

        // Act
        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut, isLostTicket: true);

        // Assert
        Assert.Equal(20_000m, result.LostTicketPenalty);
        Assert.Equal(22_000m, result.TotalFee);
    }

    [Fact]
    public void CalculateFee_LostTicketDuringGrace_OnlyPenalty()
    {
        // Arrange
        var checkIn  = Monday10Am;
        var checkOut = checkIn.AddMinutes(15);

        // Act
        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut, isLostTicket: true);

        // Assert
        Assert.Equal(20_000m, result.TotalFee);
    }

    [Fact]
    public void CalculateFee_PlatinumMemberLostTicket_DiscountNotApplyToPenalty()
    {
        // Arrange
        var checkIn  = Monday10Am;
        var checkOut = checkIn.AddHours(2);

        // Act
        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Platinum, checkIn, checkOut, isLostTicket: true);

        // Assert — 2000 - 800 discount + 20000 penalty = 21200
        Assert.Equal(21_200m, result.TotalFee);
    }

    #endregion

    #region Property-Based Tests

    private static Arbitrary<(DateTime, DateTime)> ValidDateTimePairs()
    {
        var gen =
            from days in Gen.Choose(0, 365 * 2)
            let checkIn = new DateTime(2024, 1, 1).AddDays(days)
            from minutes in Gen.Choose(1, 48 * 60)
            select (checkIn, checkIn.AddMinutes(minutes));
        return gen.ToArbitrary();
    }

    [Property]
    public Property FeeIsNeverNegative()
    {
        return Prop.ForAll(ValidDateTimePairs(), pair =>
        {
            var (checkIn, checkOut) = pair;
            foreach (var vt in Enum.GetValues<VehicleType>())
            foreach (var mt in Enum.GetValues<MembershipTier>())
            {
                var result = _calculator.CalculateFee(vt, mt, checkIn, checkOut);
                if (result.TotalFee < 0) return false;
            }
            return true;
        });
    }

    [Property]
    public Property GracePeriodIsAlwaysFree()
    {
        var gen = from days in Gen.Choose(0, 365 * 2)
                  let checkIn = new DateTime(2024, 1, 1).AddDays(days)
                  from minutes in Gen.Choose(0, 30)
                  select (checkIn, checkIn.AddMinutes(minutes));

        return Prop.ForAll(gen.ToArbitrary(), pair =>
        {
            var (checkIn, checkOut) = pair;
            var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);
            return result.BaseFee == 0m;
        });
    }

    [Property]
    public Property MembersPayLessOrEqualThanGuests()
    {
        return Prop.ForAll(ValidDateTimePairs(), pair =>
        {
            var (checkIn, checkOut) = pair;
            foreach (var vt in Enum.GetValues<VehicleType>())
            {
                var guest    = _calculator.CalculateFee(vt, MembershipTier.Guest,    checkIn, checkOut).TotalFee;
                var silver   = _calculator.CalculateFee(vt, MembershipTier.Silver,   checkIn, checkOut).TotalFee;
                var gold     = _calculator.CalculateFee(vt, MembershipTier.Gold,     checkIn, checkOut).TotalFee;
                var platinum = _calculator.CalculateFee(vt, MembershipTier.Platinum, checkIn, checkOut).TotalFee;
                if (silver > guest || gold > silver || platinum > gold) return false;
            }
            return true;
        });
    }

    [Property]
    public Property LostTicketAddsExactly20000()
    {
        return Prop.ForAll(ValidDateTimePairs(), pair =>
        {
            var (checkIn, checkOut) = pair;
            foreach (var vt in Enum.GetValues<VehicleType>())
            {
                var normal = _calculator.CalculateFee(vt, MembershipTier.Guest, checkIn, checkOut, isLostTicket: false).TotalFee;
                var lost   = _calculator.CalculateFee(vt, MembershipTier.Guest, checkIn, checkOut, isLostTicket: true ).TotalFee;
                if (lost - normal != 20_000m) return false;
            }
            return true;
        });
    }

    [Property]
    public Property DailyCapIsAlwaysRespected()
    {
        return Prop.ForAll(ValidDateTimePairs(), pair =>
        {
            var (checkIn, checkOut) = pair;
            var moto = _calculator.CalculateFee(VehicleType.Motorcycle, MembershipTier.Guest, checkIn, checkOut).BaseFee;
            var car  = _calculator.CalculateFee(VehicleType.Car,        MembershipTier.Guest, checkIn, checkOut).BaseFee;
            var suv  = _calculator.CalculateFee(VehicleType.SUV,        MembershipTier.Guest, checkIn, checkOut).BaseFee;
            return moto <= 4_000m && car <= 8_000m && suv <= 12_000m;
        });
    }

    #endregion
}