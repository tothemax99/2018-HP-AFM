using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Thorlabs.MotionControl.DeviceManagerCLI;
using Thorlabs.MotionControl.TCube.InertialMotorCLI;
using System.Threading;

namespace HPAFM_Control_1
{
    public class InterfaceThorMotorInertial
    {
        //Based on TIM101 section in C:\Program Files\Thorlabs\Kinesis\Thorlabs.MotionControl.DotNet_API.chm

        const string IMSerial = "65864344";//My device SN: 65864344
        TCubeInertialMotor InertialMotor;
        int[] setPosition = { 0, 0, 0, 0 };//integer position (steps) of each of 4 channels

        public void InitializeMotorInertial()
        {
            if (InertialMotor != null)
                throw new ApplicationException("Inertial Motor cannot be initialized as it is already initialized.");

            // Tell the device manager to get the list of all devices connected to the computer
            DeviceManagerCLI.BuildDeviceList();

            // get available TCube Stepper Motor and check our serial number is correct
            List<string> serialNumbers = DeviceManagerCLI.GetDeviceList(TCubeInertialMotor.DevicePrefix);
            if (!serialNumbers.Contains(IMSerial))
            {
                // the requested serial number is not a TSC001 or is not connected
                throw new ApplicationException("Inertial Motor serial number cannot be found in connected devices.");
            }

            // create the device
            InertialMotor = TCubeInertialMotor.CreateTCubeInertialMotor(IMSerial);
            if (InertialMotor == null)
            {
                // an error occured
                throw new ApplicationException("Inertial Motor unable to initialize connection.");
            }

            InertialMotor.Connect(IMSerial);
            
            // wait for the device settings to initialize
            if (!InertialMotor.IsSettingsInitialized())
            {
                InertialMotor.WaitForSettingsInitialized(5000);
            }

            // display info about device            
            DeviceInfo deviceInfo = InertialMotor.GetDeviceInfo();

            InertialMotor.StartPolling(250); // start the device polling
            // needs a delay so that the current enabled state can be obtained
            Thread.Sleep(500);
            // enable the channel otherwise any move is ignored 
            InertialMotor.EnableDevice();
            // needs a delay to give time for the device to be enabled
            Thread.Sleep(500);

            // call GetMotorConfiguration on the device to initialize the DeviceUnitConverter object required for real world unit parameters
            InertialMotorConfiguration InertialMotorConfiguration = InertialMotor.GetInertialMotorConfiguration(IMSerial, DeviceConfiguration.DeviceSettingsUseOptionType.UseDeviceSettings);
            ThorlabsInertialMotorSettings currentDeviceSettings = ThorlabsInertialMotorSettings.GetSettings(InertialMotorConfiguration);

            currentDeviceSettings.Drive.Channel(InertialMotorStatus.MotorChannels.Channel1).StepRate = 500;
            currentDeviceSettings.Drive.Channel(InertialMotorStatus.MotorChannels.Channel1).StepAcceleration = 100000;
            InertialMotor.SetSettings(currentDeviceSettings, true, true);
            
            //command to get actual position:
            //int newPos = InertialMotor.GetPosition(InertialMotorStatus.MotorChannels.Channel1);

            // zero the device
            InertialMotor.SetPositionAs(InertialMotorStatus.MotorChannels.Channel1, 0);
            InertialMotor.SetPositionAs(InertialMotorStatus.MotorChannels.Channel2, 0);
            InertialMotor.SetPositionAs(InertialMotorStatus.MotorChannels.Channel3, 0);
            InertialMotor.SetPositionAs(InertialMotorStatus.MotorChannels.Channel4, 0);

            setPosition[0] = 0;
            setPosition[1] = 0;
            setPosition[2] = 0;
            setPosition[3] = 0;
        }

        public void Exit()
        {
            if (InertialMotor == null)
                return;

            /*InertialMotor.Stop(InertialMotorStatus.MotorChannels.Channel1);
            InertialMotor.Stop(InertialMotorStatus.MotorChannels.Channel2);
            InertialMotor.Stop(InertialMotorStatus.MotorChannels.Channel3);
            InertialMotor.Stop(InertialMotorStatus.MotorChannels.Channel4);*/

            InertialMotor.StopPolling();
            InertialMotor.Disconnect(false);

            InertialMotor = null;
        }

        /// <summary>
        /// Blocking function, increment of 250 is about 1/50th of a turn
        /// </summary>
        /// <param name="channel">Channel 1 to 4</param>
        /// <param name="increment">Piezo stick-slip steps</param>
        /// <returns></returns>
        public void MoveMotorInc(int channel, int increment)
        {
            if (InertialMotor == null || channel > 4 || channel < 1)
                throw new ApplicationException("MoveMotorInc: inertial motor not initialized or channel out of range 1-4.");

            setPosition[channel - 1] += increment;

            switch (channel)
            {
                case 1:
                    //InertialMotor.Jog(InertialMotorStatus.MotorChannels.Channel1, InertialMotorJogDirection.Increase, 1000);
                    InertialMotor.MoveTo(InertialMotorStatus.MotorChannels.Channel1, setPosition[0], 5000);
                    break;
                case 2:
                    InertialMotor.MoveTo(InertialMotorStatus.MotorChannels.Channel2, setPosition[1], 5000);
                    break;
                case 3:
                    InertialMotor.MoveTo(InertialMotorStatus.MotorChannels.Channel3, setPosition[2], 5000);
                    break;
                case 4:
                    InertialMotor.MoveTo(InertialMotorStatus.MotorChannels.Channel4, setPosition[3], 5000);
                    break;
            }

            Thread.Sleep(50); //wait at least this long for motor to finish moving
        }
    }
}
