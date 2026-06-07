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

        // Step 2: Grace period
        var totalMinutes = (checkOut - checkIn).TotalMinutes;
        if (totalMinutes <= GracePeriodMinutes)
        {
            var penaltyOnly = isLostTicket ? LostTicketPenaltyAmount : 0m;
            return new ParkingFeeResult
            {
                BaseFee           = 0m,
                SurchargeAmount   = 0m,
                DiscountAmount    = 0m,
                LostTicketPenalty = isLostTicket ? LostTicketPenaltyAmount : 0m,
                TotalFee          = penaltyOnly,
                Breakdown         = $"Grace period. Penalty: {penaltyOnly} KHR"
            };
        }

        // Step 3: Billable hours
        var billableHours = (int)Math.Ceiling((totalMinutes - GracePeriodMinutes) / 60.0);
        if (billableHours < 1) billableHours = 1;

        // Step 4: Base fee with daily cap
        var (hourlyRate, dailyCap) = GetRateAndCap(vehicleType);
        var baseFee = Math.Min(billableHours * hourlyRate, dailyCap);

        // Step 5: Overnight fee
        var overnightFee = SessionHasOvernight(checkIn, checkOut) ? OvernightFlatFee : 0m;

        // Step 6: Surcharge
        decimal surchargeRate;
        if (isHoliday)
            surchargeRate = HolidaySurchargeRate;
        else if (checkIn.DayOfWeek == DayOfWeek.Saturday || checkIn.DayOfWeek == DayOfWeek.Sunday)
            surchargeRate = WeekendSurchargeRate;
        else
            surchargeRate = 0m;

        var surchargeAmount = baseFee * surchargeRate;

        // Step 7: Membership discount
        var discountRate = membership switch
        {
            MembershipTier.Silver   => SilverDiscountRate,
            MembershipTier.Gold     => GoldDiscountRate,
            MembershipTier.Platinum => PlatinumDiscountRate,
            _                       => 0m
        };
        var discountAmount = (baseFee + surchargeAmount) * discountRate;

        // Step 8 & 9: Lost ticket + total
        var lostTicketPenalty = isLostTicket ? LostTicketPenaltyAmount : 0m;
        var totalFee = Math.Max(0m,
            baseFee + surchargeAmount - discountAmount + overnightFee + lostTicketPenalty);

        return new ParkingFeeResult
        {
            BaseFee           = baseFee,
            SurchargeAmount   = surchargeAmount,
            DiscountAmount    = discountAmount,
            LostTicketPenalty = lostTicketPenalty,
            TotalFee          = totalFee,
            Breakdown         = $"{vehicleType} | {billableHours}h | Base:{baseFee} | " +
                                $"Surcharge:{surchargeAmount} | Discount:-{discountAmount} | " +
                                $"Overnight:{overnightFee} | Lost:{lostTicketPenalty} | Total:{totalFee} KHR"
        };
    }

    private static (decimal rate, decimal cap) GetRateAndCap(VehicleType vehicleType) =>
        vehicleType switch
        {
            VehicleType.Motorcycle => (MotorcycleRatePerHour, MotorcycleDailyCap),
            VehicleType.Car        => (CarRatePerHour,        CarDailyCap),
            VehicleType.SUV        => (SuvRatePerHour,        SuvDailyCap),
            _                      => throw new ArgumentOutOfRangeException(nameof(vehicleType))
        };

    private static bool SessionHasOvernight(DateTime checkIn, DateTime checkOut)
    {
        for (var day = checkIn.Date; day <= checkOut.Date; day = day.AddDays(1))
        {
            var overnightMoment = day.AddHours(OvernightHourThreshold);
            if (overnightMoment >= checkIn && overnightMoment < checkOut)
                return true;
        }
        return checkIn.Hour >= OvernightHourThreshold;
    }
}