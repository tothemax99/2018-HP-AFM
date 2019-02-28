using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Thorlabs.MotionControl.DeviceManagerCLI;
using Thorlabs.MotionControl.GenericMotorCLI;
using Thorlabs.MotionControl.GenericMotorCLI.ControlParameters;
using Thorlabs.MotionControl.GenericMotorCLI.AdvancedMotor;
using Thorlabs.MotionControl.GenericMotorCLI.KCubeMotor;
using Thorlabs.MotionControl.GenericMotorCLI.Settings;
using Thorlabs.MotionControl.KCube.DCServoCLI;
using System.Threading;


namespace HPAFM_Control_1
{
    public class InterfaceThorMotorLinear
    {
        //Based on KDC101 section in C:\Program Files\Thorlabs\Kinesis\Thorlabs.MotionControl.DotNet_API.chm

        const string LMSerial = "27002226";//My device SN: 27002226
        const double MinPosition = 0; //lowest allowed setpoint, and position when homed [mm]
        const double MaxPosition = 25; //max allowed position [mm]
        const decimal CountsPerMm = 3606.5M; //convert from mm to motor units
        KCubeDCServo LinearMotor;
        double setPosition = -10; //position in mm
        bool isHomed = false;
        public bool IsHomed { get { return isHomed; } }
        public double MotorPosition { get { return setPosition; } }

        public void InitializeMotorLinear()
        {
            if (LinearMotor != null)
                throw new ApplicationException("Linear Motor cannot be initialized as it is already initialized.");

            // Tell the device manager to get the list of all devices connected to the computer
            DeviceManagerCLI.BuildDeviceList();
            // get available KCube DC Servos and check our serial number is correct
            List<string> serialNumbers = DeviceManagerCLI.GetDeviceList(KCubeDCServo.DevicePrefix);
            if (!serialNumbers.Contains(LMSerial))
            {
                // the requested serial number is not a KDC101 or is not connected
                throw new ApplicationException("Linear Motor serial number cannot be found in connected devices.");
            }

            // create the device
            LinearMotor = KCubeDCServo.CreateKCubeDCServo(LMSerial);
            if (LinearMotor == null)
            {
                // an error occured
                throw new ApplicationException("Linear Motor unable to initialize connection.");
            }

            LinearMotor.Connect(LMSerial);
            
            // wait for the device settings to initialize
            if (!LinearMotor.IsSettingsInitialized())
            {
                LinearMotor.WaitForSettingsInitialized(5000);
            }

            // start the device polling
            LinearMotor.StartPolling(250);
            // needs a delay so that the current enabled state can be obtained
            Thread.Sleep(500);
            // enable the channel otherwise any move is ignored 
            LinearMotor.EnableDevice();
            // needs a delay to give time for the device to be enabled
            Thread.Sleep(500);

            // call GetMotorConfiguration on the device to initialize the DeviceUnitConverter object required for real world unit parameters
            MotorConfiguration motorSettings = LinearMotor.LoadMotorConfiguration(LMSerial);
            KCubeDCMotorSettings currentDeviceSettings = LinearMotor.MotorDeviceSettings as KCubeDCMotorSettings;

            /*adjust settings if necessary
            // display info about device
            DeviceInfo deviceInfo = LinearMotor.GetDeviceInfo();
            VelocityParameters velPars = LinearMotor.GetVelocityParams();
            velPars.MaxVelocity = velocity;
            LinearMotor.SetVelocityParams(velPars);
            */

            isHomed = LinearMotor.Status.IsHomed;

            setPosition = (double)(LinearMotor.Position / CountsPerMm);
        }

        public void Exit()
        {
            if (LinearMotor == null)
                return;

            if(LinearMotor.Status.IsMoving)
                LinearMotor.Stop(1000);

            LinearMotor.Disconnect(false);

            LinearMotor = null;
        }

        public void HomeMotor()
        {//the function is blocking
            if (LinearMotor == null)
                throw new ApplicationException("HomeMotor: linear motor not initialized.");

            if (LinearMotor.Status.IsHomed)
                throw new ApplicationException("HomeMotor: linear motor is already homed.");


            LinearMotor.Home(60000);

            setPosition = MinPosition;

            isHomed = true;
        }

        /*
        decimal GetMotorPosition()
        {//motor position in [mm]
            if (LinearMotor == null || !isHomed)
                return -1;

            return LinearMotor.Position;
        }*/
        
        public void MoveMotorInc(double increment)
        {//position is in [mm], the function is blocking
            if (LinearMotor == null || !isHomed)
                throw new ApplicationException("MoveMotorInc: linear motor not initialized or not homed.");

            if (setPosition + increment < MinPosition || setPosition + increment > MaxPosition)
                throw new ArgumentOutOfRangeException("MoveMotorInc: linear motor value out of range, current=" + setPosition.ToString() + ", increment=" + increment.ToString());

            setPosition += increment;

            LinearMotor.MoveTo((decimal)setPosition * CountsPerMm, 60000);
        }

        public void MoveMotorAbs(double position)
        {//position is in [mm], the function is blocking
            if (LinearMotor == null || !isHomed)
                throw new ApplicationException("MoveMotorAbs: linear motor not initialized or not homed.");

            if (position < MinPosition || position > MaxPosition)
                throw new ArgumentOutOfRangeException("MoveMotorAbs: linear motor value out of range, setpoint=" + position.ToString());

            setPosition = position;

            LinearMotor.MoveTo((decimal)setPosition * CountsPerMm, 60000);
        }
    }
}
