using Moq;
using SmartPark.Core.Interfaces;
using SmartPark.Core.Models;
using SmartPark.Core.Services;

namespace SmartPark.Tests;

public class ParkingSessionManagerTests
{
    private readonly Mock<IPaymentGateway>      _paymentStub      = new();
    private readonly Mock<INotificationService> _notificationStub = new();
    private readonly Mock<IMembershipService>   _membershipStub   = new();
    private readonly Mock<IParkingRepository>   _repoStub         = new();
    private readonly Mock<IDateTimeProvider>    _dateTimeStub     = new();
    private readonly ParkingFeeCalculator       _feeCalculator    = new();
    private readonly ParkingSessionManager      _manager;

    private static readonly DateTime FixedCheckIn  = new(2026, 3, 16, 10, 0, 0);
    private static readonly DateTime FixedCheckOut = new(2026, 3, 16, 12, 0, 0);

    public ParkingSessionManagerTests()
    {
        _manager = new ParkingSessionManager(
            _feeCalculator,
            _paymentStub.Object,
            _notificationStub.Object,
            _membershipStub.Object,
            _repoStub.Object,
            _dateTimeStub.Object);
    }

    private ParkingTicket MakeActiveTicket(string plate = "PP-1234", string ticketId = "TICKET01")
    {
        return new ParkingTicket
        {
            TicketId    = ticketId,
            CheckInTime = FixedCheckIn,
            Vehicle     = new Vehicle
            {
                LicensePlate = plate,
                Type         = VehicleType.Car,
                Membership   = MembershipTier.Guest
            }
        };
    }

    #region CheckIn — Happy Path

    [Fact]
    public async Task CheckInAsync_NewVehicle_LooksUpMembershipOnce()
    {
        // Arrange
        _membershipStub.Setup(m => m.GetMembershipTier("PP-9999")).Returns(MembershipTier.Guest);
        _repoStub.Setup(r => r.GetActiveTicketByPlateAsync("PP-9999")).ReturnsAsync((ParkingTicket?)null);
        _dateTimeStub.Setup(d => d.Now).Returns(FixedCheckIn);

        // Act
        await _manager.CheckInAsync("PP-9999", VehicleType.Car);

        // Assert
        _membershipStub.Verify(m => m.GetMembershipTier("PP-9999"), Times.Once);
    }

    [Fact]
    public async Task CheckInAsync_NewVehicle_SavesTicketToRepository()
    {
        // Arrange
        _membershipStub.Setup(m => m.GetMembershipTier("PP-1111")).Returns(MembershipTier.Gold);
        _repoStub.Setup(r => r.GetActiveTicketByPlateAsync("PP-1111")).ReturnsAsync((ParkingTicket?)null);
        _dateTimeStub.Setup(d => d.Now).Returns(FixedCheckIn);

        // Act
        var ticket = await _manager.CheckInAsync("PP-1111", VehicleType.SUV);

        // Assert
        _repoStub.Verify(r => r.SaveTicketAsync(It.IsAny<ParkingTicket>()), Times.Once);
        Assert.Equal("PP-1111", ticket.Vehicle.LicensePlate);
        Assert.Equal(MembershipTier.Gold, ticket.Vehicle.Membership);
    }

    #endregion

    #region CheckIn — Validation

    [Fact]
    public async Task CheckInAsync_DuplicateActivePlate_ThrowsAndDoesNotSave()
    {
        // Arrange
        var existingTicket = MakeActiveTicket("PP-DUPE");
        _membershipStub.Setup(m => m.GetMembershipTier("PP-DUPE")).Returns(MembershipTier.Guest);
        _repoStub.Setup(r => r.GetActiveTicketByPlateAsync("PP-DUPE")).ReturnsAsync(existingTicket);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _manager.CheckInAsync("PP-DUPE", VehicleType.Car));

        _repoStub.Verify(r => r.SaveTicketAsync(It.IsAny<ParkingTicket>()), Times.Never);
    }

    #endregion

    #region CheckOut — Happy Path

    [Fact]
    public async Task CheckOutAsync_ValidTicket_ProcessesPaymentAndSendsReceipt()
    {
        // Arrange
        var ticket = MakeActiveTicket();
        _repoStub.Setup(r => r.GetTicketByIdAsync("TICKET01")).ReturnsAsync(ticket);
        _dateTimeStub.Setup(d => d.Now).Returns(FixedCheckOut);
        _paymentStub.Setup(p => p.ProcessPaymentAsync("TICKET01", It.IsAny<decimal>())).ReturnsAsync(true);

        // Act
        var result = await _manager.CheckOutAsync("TICKET01", "012-345-678");

        // Assert
        Assert.Equal(2_000m, result.TotalFee);
        _paymentStub.Verify(p => p.ProcessPaymentAsync("TICKET01", 2_000m), Times.Once);
        _notificationStub.Verify(n => n.SendReceiptAsync("012-345-678", It.IsAny<string>()), Times.Once);
    }

    #endregion

    #region CheckOut — Payment Failure

    [Fact]
    public async Task CheckOutAsync_PaymentFails_ThrowsAndDoesNotUpdateTicket()
    {
        // Arrange
        var ticket = MakeActiveTicket();
        _repoStub.Setup(r => r.GetTicketByIdAsync("TICKET01")).ReturnsAsync(ticket);
        _dateTimeStub.Setup(d => d.Now).Returns(FixedCheckOut);
        _paymentStub.Setup(p => p.ProcessPaymentAsync(It.IsAny<string>(), It.IsAny<decimal>())).ReturnsAsync(false);

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() =>
            _manager.CheckOutAsync("TICKET01", "012-345-678"));

        _repoStub.Verify(r => r.UpdateTicketAsync(It.IsAny<ParkingTicket>()), Times.Never);
        _notificationStub.Verify(n => n.SendReceiptAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region CheckOut — Notification Failure

    [Fact]
    public async Task CheckOutAsync_NotificationFails_CheckoutStillSucceeds()
    {
        // Arrange
        var ticket = MakeActiveTicket();
        _repoStub.Setup(r => r.GetTicketByIdAsync("TICKET01")).ReturnsAsync(ticket);
        _dateTimeStub.Setup(d => d.Now).Returns(FixedCheckOut);
        _paymentStub.Setup(p => p.ProcessPaymentAsync(It.IsAny<string>(), It.IsAny<decimal>())).ReturnsAsync(true);
        _notificationStub.Setup(n => n.SendReceiptAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("SMS down"));

        // Act
        var result = await _manager.CheckOutAsync("TICKET01", "012-345-678");

        // Assert
        Assert.Equal(2_000m, result.TotalFee);
        _repoStub.Verify(r => r.UpdateTicketAsync(It.IsAny<ParkingTicket>()), Times.Once);
    }

    #endregion

    #region CheckOut — Validation

    [Fact]
    public async Task CheckOutAsync_TicketNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        _repoStub.Setup(r => r.GetTicketByIdAsync("MISSING")).ReturnsAsync((ParkingTicket?)null);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _manager.CheckOutAsync("MISSING", "012-345-678"));
    }

    [Fact]
    public async Task CheckOutAsync_AlreadyCheckedOut_ThrowsInvalidOperationException()
    {
        // Arrange
        var ticket = MakeActiveTicket();
        ticket.CheckOutTime = FixedCheckOut;
        _repoStub.Setup(r => r.GetTicketByIdAsync("TICKET01")).ReturnsAsync(ticket);
        _dateTimeStub.Setup(d => d.Now).Returns(FixedCheckOut.AddHours(1));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _manager.CheckOutAsync("TICKET01", "012-345-678"));
    }

    #endregion

    #region Verify Interaction Order

    [Fact]
    public async Task CheckOutAsync_PaymentBeforeTicketUpdate()
    {
        // Arrange
        var callOrder = new List<string>();
        var ticket = MakeActiveTicket();
        _repoStub.Setup(r => r.GetTicketByIdAsync("TICKET01")).ReturnsAsync(ticket);
        _dateTimeStub.Setup(d => d.Now).Returns(FixedCheckOut);
        _paymentStub.Setup(p => p.ProcessPaymentAsync(It.IsAny<string>(), It.IsAny<decimal>()))
            .Callback(() => callOrder.Add("payment")).ReturnsAsync(true);
        _repoStub.Setup(r => r.UpdateTicketAsync(It.IsAny<ParkingTicket>()))
            .Callback(() => callOrder.Add("update")).Returns(Task.CompletedTask);
        _notificationStub.Setup(n => n.SendReceiptAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => callOrder.Add("receipt")).Returns(Task.CompletedTask);

        // Act
        await _manager.CheckOutAsync("TICKET01", "012-345-678");

        // Assert
        Assert.Equal(new[] { "payment", "update", "receipt" }, callOrder);
    }

    #endregion
}