using Moq;
using SmartPark.Core.Interfaces;
using SmartPark.Core.Models;
using SmartPark.Core.Services;

namespace SmartPark.Tests.IntegrationTests;

public class ParkingFlowIntegrationTests
{
    private readonly ParkingFeeCalculator      _feeCalculator = new();
    private readonly InMemoryParkingRepository _repository    = new();
    private readonly Mock<IPaymentGateway>      _paymentStub  = new();
    private readonly Mock<INotificationService> _notificationStub = new();
    private readonly ParkingSessionManager      _manager;

    private DateTime _currentTime = new(2026, 3, 16, 10, 0, 0);

    public ParkingFlowIntegrationTests()
    {
        var dateTimeStub = new Mock<IDateTimeProvider>();
        dateTimeStub.Setup(d => d.Now).Returns(() => _currentTime);

        var membershipStub = new Mock<IMembershipService>();
        membershipStub.Setup(m => m.GetMembershipTier(It.IsAny<string>())).Returns(MembershipTier.Guest);

        _paymentStub.Setup(p => p.ProcessPaymentAsync(It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(true);

        _manager = new ParkingSessionManager(
            _feeCalculator,
            _paymentStub.Object,
            _notificationStub.Object,
            membershipStub.Object,
            _repository,
            dateTimeStub.Object);
    }

    #region Full Parking Flow

    [Fact]
    public async Task FullFlow_CarFor2Hours_FeeIs2000()
    {
        // Arrange
        _currentTime = new DateTime(2026, 3, 16, 10, 0, 0);
        var ticket = await _manager.CheckInAsync("CAR-001", VehicleType.Car);

        // Act
        _currentTime = new DateTime(2026, 3, 16, 12, 30, 0);
        var result = await _manager.CheckOutAsync(ticket.TicketId, "012-000-001");

        // Assert
        Assert.Equal(2_000m, result.TotalFee);
    }

    [Fact]
    public async Task FullFlow_MotorcycleWithinGrace_FeeIsZero()
    {
        // Arrange
        _currentTime = new DateTime(2026, 3, 16, 9, 0, 0);
        var ticket = await _manager.CheckInAsync("MOTO-001", VehicleType.Motorcycle);

        // Act
        _currentTime = new DateTime(2026, 3, 16, 9, 15, 0);
        var result = await _manager.CheckOutAsync(ticket.TicketId, "012-000-002");

        // Assert
        Assert.Equal(0m, result.TotalFee);
    }

    #endregion

    #region Multiple Vehicles

    [Fact]
    public async Task MultipleVehicles_CheckOutOne_OtherRemainActive()
    {
        // Arrange
        _currentTime = new DateTime(2026, 3, 16, 8, 0, 0);
        var t1 = await _manager.CheckInAsync("PLATE-A", VehicleType.Car);
        var t2 = await _manager.CheckInAsync("PLATE-B", VehicleType.Car);
        var t3 = await _manager.CheckInAsync("PLATE-C", VehicleType.Car);

        // Act
        _currentTime = new DateTime(2026, 3, 16, 10, 0, 0);
        await _manager.CheckOutAsync(t2.TicketId, "012-000-003");

        // Assert
        var active = (await _repository.GetAllActiveTicketsAsync()).ToList();
        Assert.Equal(2, active.Count);
        Assert.Contains(active, t => t.TicketId == t1.TicketId);
        Assert.Contains(active, t => t.TicketId == t3.TicketId);
    }

    #endregion

    #region Error Recovery

    [Fact]
    public async Task DuplicateCheckIn_RejectsSecond_FirstTicketStillActive()
    {
        // Arrange
        _currentTime = new DateTime(2026, 3, 16, 9, 0, 0);
        var ticket = await _manager.CheckInAsync("DUPE-PLATE", VehicleType.Car);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _manager.CheckInAsync("DUPE-PLATE", VehicleType.Car));

        var active = (await _repository.GetAllActiveTicketsAsync()).ToList();
        Assert.Single(active);
        Assert.Equal(ticket.TicketId, active[0].TicketId);
    }

    [Fact]
    public async Task FailedPayment_TicketRemainsActive()
    {
        // Arrange
        _paymentStub.Setup(p => p.ProcessPaymentAsync(It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(false);

        _currentTime = new DateTime(2026, 3, 16, 9, 0, 0);
        var ticket = await _manager.CheckInAsync("FAIL-PAY", VehicleType.Car);

        // Act
        _currentTime = new DateTime(2026, 3, 16, 11, 0, 0);
        await Assert.ThrowsAsync<Exception>(() =>
            _manager.CheckOutAsync(ticket.TicketId, "012-000-004"));

        // Assert
        var active = (await _repository.GetAllActiveTicketsAsync()).ToList();
        Assert.Contains(active, t => t.TicketId == ticket.TicketId);
    }

    #endregion

    #region Edge-to-Edge Scenarios

    [Fact]
    public async Task EdgeToEdge_LostTicketDuringGrace_OnlyPenalty()
    {
        // Arrange
        _currentTime = new DateTime(2026, 3, 16, 10, 0, 0);
        var ticket = await _manager.CheckInAsync("LOST-GRACE", VehicleType.Car);

        // Act
        _currentTime = new DateTime(2026, 3, 16, 10, 20, 0);
        var result = await _manager.CheckOutAsync(ticket.TicketId, "012-000-006", isLostTicket: true);

        // Assert
        Assert.Equal(0m, result.BaseFee);
        Assert.Equal(20_000m, result.TotalFee);
    }

    #endregion
}