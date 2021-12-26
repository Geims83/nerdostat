using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Amqp.Framing;
using Microsoft.Azure.Devices.Client;
using Nerdostat.Shared;
using Newtonsoft.Json;

namespace Nerdostat.Device
{
    public class Hub
    {
        private int AzureStatusPinNumber = 18;

        private OuputPin AzureStatusLed;

        private readonly Thermostat thermo;

        private DeviceClient client;

        private bool IsTestDevice;

        public static async Task<Hub> Initialize(string connectionString, bool? testDevice, Thermostat thermo)
        {
            var hub = new Hub(connectionString, testDevice, thermo);
            await hub.ConfigureCallbacks();

            return hub;
        }

        private Hub(string connectionString, bool? _testDevice, Thermostat _thermo)
        {
            client ??= DeviceClient.CreateFromConnectionString(connectionString);
            IsTestDevice = _testDevice ?? false;
            this.thermo = _thermo;
            AzureStatusLed = new OuputPin(AzureStatusPinNumber);
        }

        private async Task ConfigureCallbacks()
        {
            await client.SetMethodHandlerAsync(
                DeviceMethods.ReadNow,
                RefreshThermoData,
                null
            );

            await client.SetMethodHandlerAsync(
                DeviceMethods.SetManualSetpoint,
                OverrideSetpoint,
                null
            );

            await client.SetMethodHandlerAsync(
                DeviceMethods.ClearManualSetPoint,
                ClearSetpoint,
                null
            );

            await client.SetMethodHandlerAsync(
                DeviceMethods.SetAwayOn,
                SetAwayOn,
                null
            );

        }

        private async Task<MethodResponse> RefreshThermoData(MethodRequest methodRequest, object userContext)
        {
            var thermoData = await thermo.Refresh();
            var message = new APIMessage(){
                Timestamp = DateTime.Now,
                Temperature = thermoData.Temperature,
                Humidity = thermoData.Humidity,
                CurrentSetpoint = thermoData.CurrentSetpoint,
                HeaterOn = Convert.ToInt64(thermoData.HeaterOn),
                IsHeaterOn = false,
                OverrideEnd = 0
            };

            var stringData = JsonConvert.SerializeObject(message);
            var byteData = Encoding.UTF8.GetBytes(stringData);
            return new MethodResponse(byteData, 200);
        }

        private async Task<MethodResponse> OverrideSetpoint(MethodRequest methodRequest, object userContext)
        {
            var input = JsonConvert.DeserializeObject<SetPointMessage>(methodRequest.DataAsJson);
            thermo.OverrideSetpoint(
                Convert.ToDecimal(input.Setpoint),
                Convert.ToInt32(input.Hours));
            return await RefreshThermoData(methodRequest, userContext);
        }

        private async Task<MethodResponse> ClearSetpoint(MethodRequest methodRequest, object userContext)
        {
            thermo.ReturnToProgram();
            return await RefreshThermoData(methodRequest, userContext);
        }

        private async Task<MethodResponse> SetAwayOn(MethodRequest methodRequest, object userContext)
        {
            thermo.SetAway();
            return await RefreshThermoData(methodRequest, userContext);
        }

        public async Task SendIotMessage(APIMessage message)
        {
            var cts = new CancellationTokenSource();
            var blink = AzureStatusLed.Blink((decimal)0.1, (decimal)0.1, cts.Token);
            var messageBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));
            var iotMessage = new Message(messageBytes);

            if (IsTestDevice)
                iotMessage.Properties.Add("testDevice", "true");

            try
            {
                await client.SendEventAsync(iotMessage);
                cts.Cancel();
                AzureStatusLed.TurnOn();
            }
            catch
            {
                cts.Cancel();
                AzureStatusLed.TurnOff();
            }
        }
    }
}