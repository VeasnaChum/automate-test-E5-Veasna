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

}