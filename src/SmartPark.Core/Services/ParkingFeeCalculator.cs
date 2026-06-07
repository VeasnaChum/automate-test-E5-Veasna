using SmartPark.Core.Models;

namespace SmartPark.Core.Services;

public class ParkingFeeCalculator
{
    private const decimal MotorcycleRatePerHour = 500m;
    private const decimal CarRatePerHour        = 1_000m;
    private const decimal SuvRatePerHour        = 1_500m;

    private const decimal MotorcycleDailyCap = 4_000m;
    private const decimal CarDailyCap        = 8_000m;
    private const decimal SuvDailyCap        = 12_000m;

    private const int     GracePeriodMinutes     = 30;
    private const decimal OvernightFlatFee       = 2_000m;
    private const int     OvernightHourThreshold = 22;

    private const decimal WeekendSurchargeRate = 0.20m;
    private const decimal HolidaySurchargeRate = 0.50m;

    private const decimal SilverDiscountRate   = 0.10m;
    private const decimal GoldDiscountRate     = 0.25m;
    private const decimal PlatinumDiscountRate = 0.40m;

    private const decimal LostTicketPenaltyAmount = 20_000m;

    public ParkingFeeResult CalculateFee(
        VehicleType vehicleType,
        MembershipTier membership,
        DateTime checkIn,
        DateTime checkOut,
        bool isLostTicket = false,
        bool isHoliday = false)
    {
        // Step 1: Validate
        if (checkOut < checkIn)
            throw new ArgumentException("Check-out time cannot be before check-in time.");

        throw new NotImplementedException("More steps to be implemented via TDD.");
    }
}