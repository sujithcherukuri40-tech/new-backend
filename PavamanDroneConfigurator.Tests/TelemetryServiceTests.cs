using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;
using PavamanDroneConfigurator.Infrastructure.Services;
using Xunit;

namespace PavamanDroneConfigurator.Tests;

public class TelemetryServiceTests
{
    [Fact]
    public async Task Connect_Negotiating_To_Active()
    {
        var (service, connection, state) = CreateService();
        service.Start();

        state.IsConnected = true;
        connection.Raise(x => x.ConnectionStateChanged += null, null, true);
        await Task.Delay(650);

        connection.Raise(x => x.HeartbeatDataReceived += null, this, new HeartbeatDataEventArgs
        {
            SystemId = 1,
            VehicleType = 2,
            CustomMode = 3,
            BaseMode = 0x80,
            SystemStatus = 4,
            IsArmed = true
        });

        connection.Raise(x => x.AttitudeReceived += null, this, new AttitudeEventArgs
        {
            Roll = 1,
            Pitch = 2,
            Yaw = 3,
            RollSpeed = 0.1,
            PitchSpeed = 0.1,
            YawSpeed = 0.2
        });

        await WaitUntilAsync(() => service.CurrentState == TelemetryServiceState.TelemetryActive, TimeSpan.FromSeconds(2));

        Assert.Equal(TelemetryServiceState.TelemetryActive, service.CurrentState);
        Assert.True(service.CurrentHealth.IsReceivingTelemetry);

        service.Dispose();
    }

    [Fact]
    public async Task Active_To_Stale_To_Recovered()
    {
        var (service, connection, state) = CreateService();
        service.Start();

        state.IsConnected = true;
        connection.Raise(x => x.ConnectionStateChanged += null, null, true);
        await Task.Delay(650);
        RaiseHeartbeat(connection);
        RaiseAttitude(connection);

        await WaitUntilAsync(() => service.CurrentState == TelemetryServiceState.TelemetryActive, TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => service.CurrentState == TelemetryServiceState.TelemetryStaleRecovering, TimeSpan.FromSeconds(6));

        RaiseHeartbeat(connection);
        RaiseAttitude(connection);

        await WaitUntilAsync(() => service.CurrentState == TelemetryServiceState.TelemetryActive, TimeSpan.FromSeconds(2));

        Assert.Equal(TelemetryServiceState.TelemetryActive, service.CurrentState);

        service.Dispose();
    }

    [Fact]
    public async Task Disconnect_Stops_Retry_Bursts()
    {
        var (service, connection, state) = CreateService();
        service.Start();

        state.IsConnected = true;
        connection.Raise(x => x.ConnectionStateChanged += null, null, true);

        await Task.Delay(400);
        var sentBeforeDisconnect = state.StreamRequestCount;

        state.IsConnected = false;
        connection.Raise(x => x.ConnectionStateChanged += null, null, false);

        await Task.Delay(2600);
        var sentAfterDisconnect = state.StreamRequestCount;

        Assert.Equal(TelemetryServiceState.Disconnected, service.CurrentState);
        Assert.Equal(sentBeforeDisconnect, sentAfterDisconnect);

        service.Dispose();
    }

    [Fact]
    public async Task ParsingGuards_Ignore_Unknown_Heading_And_Unknown_Current()
    {
        var (service, connection, state) = CreateService();
        service.Start();

        state.IsConnected = true;
        connection.Raise(x => x.ConnectionStateChanged += null, null, true);
        await Task.Delay(650);
        RaiseHeartbeat(connection);

        connection.Raise(x => x.GlobalPositionIntReceived += null, this, new GlobalPositionIntEventArgs
        {
            Latitude = 17.111111,
            Longitude = 78.222222,
            AltitudeMsl = 500,
            AltitudeRelative = 20,
            VelocityX = 1,
            VelocityY = 2,
            VelocityZ = 0,
            Heading = 123
        });

        connection.Raise(x => x.GlobalPositionIntReceived += null, this, new GlobalPositionIntEventArgs
        {
            Latitude = 17.111112,
            Longitude = 78.222223,
            AltitudeMsl = 500,
            AltitudeRelative = 20,
            VelocityX = 1,
            VelocityY = 2,
            VelocityZ = 0,
            Heading = double.NaN
        });

        connection.Raise(x => x.SysStatusReceived += null, this, new SysStatusEventArgs
        {
            BatteryVoltage = 23.5,
            BatteryCurrent = 4.2,
            BatteryRemaining = 80
        });

        connection.Raise(x => x.SysStatusReceived += null, this, new SysStatusEventArgs
        {
            BatteryVoltage = 23.4,
            BatteryCurrent = double.NaN,
            BatteryRemaining = -1
        });

        await Task.Delay(100);
        var snapshot = service.CurrentTelemetry;

        Assert.Equal(123, snapshot.Heading, 3);
        Assert.Equal(4.2, snapshot.BatteryCurrent, 3);
        Assert.Equal(-1, snapshot.BatteryRemaining);

        service.Dispose();
    }

    [Fact]
    public async Task Availability_Event_Debounced_On_State_Change()
    {
        var (service, connection, state) = CreateService();
        service.Start();

        var availabilityEvents = 0;
        service.TelemetryAvailabilityChanged += (_, _) => availabilityEvents++;

        state.IsConnected = true;
        connection.Raise(x => x.ConnectionStateChanged += null, null, true);
        await Task.Delay(650);
        RaiseHeartbeat(connection);
        RaiseAttitude(connection);

        await WaitUntilAsync(() => service.CurrentState == TelemetryServiceState.TelemetryActive, TimeSpan.FromSeconds(2));

        RaiseAttitude(connection);
        RaiseAttitude(connection);
        await Task.Delay(200);

        state.IsConnected = false;
        connection.Raise(x => x.ConnectionStateChanged += null, null, false);

        await Task.Delay(100);

        Assert.Equal(2, availabilityEvents);

        service.Dispose();
    }

    private static void RaiseHeartbeat(Mock<IConnectionService> connection)
    {
        connection.Raise(x => x.HeartbeatDataReceived += null, null, new HeartbeatDataEventArgs
        {
            SystemId = 1,
            VehicleType = 2,
            CustomMode = 3,
            BaseMode = 0x80,
            SystemStatus = 4,
            IsArmed = true
        });
    }

    private static void RaiseAttitude(Mock<IConnectionService> connection)
    {
        connection.Raise(x => x.AttitudeReceived += null, null, new AttitudeEventArgs
        {
            Roll = 1,
            Pitch = 2,
            Yaw = 3,
            RollSpeed = 0.1,
            PitchSpeed = 0.1,
            YawSpeed = 0.2
        });
    }

    private static (TelemetryService Service, Mock<IConnectionService> Connection, ConnectionState State) CreateService()
    {
        var state = new ConnectionState();
        var connection = new Mock<IConnectionService>();

        connection.SetupGet(x => x.IsConnected).Returns(() => state.IsConnected);
        connection.Setup(x => x.SendTelemetryNegotiationCommand(It.IsAny<TelemetryNegotiationCommand>()))
            .Callback<TelemetryNegotiationCommand>(cmd =>
            {
                if (cmd.Type == TelemetryNegotiationCommandType.RequestDataStream)
                {
                    Interlocked.Increment(ref state.StreamRequestCount);
                }
                else if (cmd.Type == TelemetryNegotiationCommandType.SetMessageInterval)
                {
                    Interlocked.Increment(ref state.SetIntervalCount);
                }
            });

        var service = new TelemetryService(new NullLogger<TelemetryService>(), connection.Object);
        return (service, connection, state);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var started = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - started > timeout)
            {
                throw new TimeoutException("Condition timed out.");
            }

            await Task.Delay(50);
        }
    }

    private sealed class ConnectionState
    {
        public bool IsConnected;
        public int StreamRequestCount;
        public int SetIntervalCount;
    }
}
