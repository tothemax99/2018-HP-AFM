using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HPAFM_Control_1
{
    public class InterfacePIMotors
    {
        //Based on example in C:\Users\Public\PI\Mercury\Samples\VC#\DataRecorder

        const int PIBaud = 115200;
        const string PISerial = "PI C-663 Mercury-Step  SN 0017550051\n";
        const int BlockingWait = 100; //poll the motor movement at this interval if function is desired to be blocking
        const double MaxPosition = 52.0; //highest allowed set position, and position reached when positive homed [mm]
        const double RefPosition = 26.0; //position reached when middle homed [mm]
        const double MinPosition = 0.0; //lowest allowed set position, and position reached when negative homed [mm]

        struct PIMotor
        {
            public double setPosition; //in [mm]
            public int controllerID;
            public string axisID;
            public bool isHomed;
            public double minLimit; //soft limits to allow limiting of range for device safety
            public double maxLimit;
        }
        public enum HomingType { Negative, Middle, Positive };

        PIMotor[] motors;
        int daisyPortID;

        public void InitializePIMotors()
        {
            if (motors != null)
                throw new ApplicationException("PI Motors Controller cannot be initialized as it is already initialized.");

            StringBuilder IdnUsbControllers = new StringBuilder(1024);
            string sFilter = "";
            PI.GCS2.EnumerateUSB(IdnUsbControllers, 1024, sFilter);
            if (IdnUsbControllers.Length < 1 || !IdnUsbControllers.ToString().Contains(PISerial))
                throw new ApplicationException("PI Motors controller could not be found.");

            int[] bFlags = new int[1];
            int daisyNum = 0;

            daisyPortID = PI.GCS2.OpenUSBDaisyChain(PISerial, ref daisyNum, IdnUsbControllers, 1024);
            if (daisyPortID < 0)
                throw new ApplicationException("PI Motors controller could not open connection.");

            if (daisyNum != 2)
                throw new ApplicationException("PI Motors controller unexpected number of devices connected.");

            int id1 = PI.GCS2.ConnectDaisyChainDevice(daisyPortID, 1); //first controller ID
            int id2 = PI.GCS2.ConnectDaisyChainDevice(daisyPortID, 2); //second controller ID          

            //turn on servo motors
            bFlags[0] = 1;
            PI.GCS2.SVO(id1, "1", bFlags); //controller 1, axis 1
            PI.GCS2.SVO(id2, "1", bFlags); //controller 2, axis 1

            //confirm servo motors are on
            PI.GCS2.qSVO(id1, "1", bFlags);
            if (bFlags[0] != 1)
                throw new ApplicationException("PI Motors could not turn on controller #1.");
            PI.GCS2.qSVO(id2, "1", bFlags);
            if (bFlags[0] != 1)
                throw new ApplicationException("PI Motors could not turn on controller #2.");

            double[] dVals = new double[1];
            motors = new PIMotor[2];

            motors[0] = new PIMotor();
            motors[0].axisID = "1";
            motors[0].controllerID = id1;
            PI.GCS2.qFRF(id1, "1", bFlags);//is the motor homed?
            motors[0].isHomed = (bFlags[0] == 1);
            PI.GCS2.qMOV(id1, "1", dVals);//get current position
            motors[0].setPosition = dVals[0];
            motors[0].minLimit = MinPosition; //default limits are full range
            motors[0].maxLimit = MaxPosition;

            motors[1] = new PIMotor();
            motors[1].axisID = "1";
            motors[1].controllerID = id2;
            PI.GCS2.qFRF(id2, "1", bFlags);//is the motor homed?
            motors[1].isHomed = (bFlags[0] == 1);
            PI.GCS2.qMOV(id2, "1", dVals);//get current position
            motors[1].setPosition = dVals[0];
            motors[1].minLimit = MinPosition; //default limits are full range
            motors[1].maxLimit = MaxPosition;
        }

        public bool IsMotorHomed(int motor)
        {
            if (motors == null || motor < 1 || motor > 2)
                throw new ApplicationException("IsMotorHomed: PI Motors not initialized or motor not=(1 or 2).");

            return motors[motor - 1].isHomed;
        }

        public void Exit()
        {
            if (motors == null)
                return;

            //turn off servo motors - won't do it here because then it becomes possible to lose track of actual position while still believing device is homed
            /*
            int[] bFlags = new int[1];
            bFlags[0] = 0;
            PI.GCS2.SVO(motors[0].controllerID, motors[0].axisID, bFlags); //controller 1, axis 1
            PI.GCS2.SVO(motors[1].controllerID, motors[1].axisID, bFlags); //controller 2, axis 1
            */

            PI.GCS2.CloseDaisyChain(daisyPortID);

            motors = null;
        }

        public void HomeMotor(int motor, HomingType homingType, bool blocking)
        {
            if (motors == null || motor < 1 || motor > 2)
                throw new ApplicationException("HomeMotor: PI Motors not initialized or motor not=(1 or 2).");

            if (motors[motor - 1].isHomed)
                throw new ApplicationException("HomeMotor: selected motor is already homed, motor=" + motor.ToString());

            switch (homingType)
            {
                case HomingType.Negative:
                    PI.GCS2.FNL(motors[motor - 1].controllerID, motors[motor - 1].axisID);
                    motors[motor - 1].setPosition = MinPosition;
                    break;
                case HomingType.Middle:
                    PI.GCS2.FRF(motors[motor - 1].controllerID, motors[motor - 1].axisID);
                    motors[motor - 1].setPosition = RefPosition;
                    break;
                case HomingType.Positive:
                    PI.GCS2.FPL(motors[motor - 1].controllerID, motors[motor - 1].axisID);
                    motors[motor - 1].setPosition = MaxPosition;
                    break;
            }

            if (blocking)
            {
                int[] bFlags = new int[1];
                do
                {
                    System.Threading.Thread.Sleep(BlockingWait);
                    PI.GCS2.qFRF(motors[motor - 1].controllerID, motors[motor - 1].axisID, bFlags);//is the motor homed?
                } while (bFlags[0] != 1); //wait until motor is homed
            }

            motors[motor - 1].isHomed = true;
        }

        public double GetMotorPosition(int motor)
        {//returns actual position in [mm]
            if (motors == null || motor < 1 || motor > 2)
                throw new ApplicationException("GetMotorPosition: PI Motors not initialized or motor not=(1 or 2).");

            if (!motors[motor - 1].isHomed)
                throw new ApplicationException("GetMotorPosition: Motor not homed prior to requesting position, motor=" + motor.ToString());

            double[] dVals = new double[1];
            PI.GCS2.qMOV(motors[motor - 1].controllerID, motors[motor - 1].axisID, dVals);//get current position
            return dVals[0];
        }

        public double GetMotorSetpt(int motor)
        {//returns set position in [mm]
            if (motors == null || motor < 1 || motor > 2)
                throw new ApplicationException("GetMotorSetpt: PI Motors not initialized or motor not=(1 or 2).");

            if (!motors[motor - 1].isHomed)
                throw new ApplicationException("GetMotorSetpt: Motor not homed prior to requesting position, motor=" + motor.ToString());
            
            return motors[motor - 1].setPosition;
        }

        public void MoveMotorInc(int motor, double increment, bool blocking)
        {//increment in [mm]
            if (motors == null || motor < 1 || motor > 2)
                throw new ApplicationException("MoveMotorInc: PI Motors not initialized or motor not=(1 or 2).");

            if (!motors[motor - 1].isHomed)
                throw new ApplicationException("MoveMotorInc: Motor not homed prior to requesting move, motor=" + motor.ToString());

            double[] dVals = new double[1];
            dVals[0] = motors[motor - 1].setPosition + increment;
            if (dVals[0] < motors[motor - 1].minLimit || dVals[0] > motors[motor - 1].maxLimit)
                throw new ArgumentOutOfRangeException("MoveMotorInc: requested move exceeds motor limits, motor=" + motor.ToString() + ", current position=" + motors[motor - 1].setPosition.ToString() + " and delta=" + increment.ToString());

            PI.GCS2.MOV(motors[motor - 1].controllerID, motors[motor - 1].axisID, dVals);

            if (blocking)
            {
                int[] bFlags = new int[1];
                do
                {
                    System.Threading.Thread.Sleep(BlockingWait);
                    PI.GCS2.IsMoving(motors[motor - 1].controllerID, motors[motor - 1].axisID, bFlags);//is the motor moving?
                } while (bFlags[0] != 0); //wait until motor stops moving
            }

            motors[motor - 1].setPosition = dVals[0];
        }

        public void MoveMotorAbs(int motor, double position, bool blocking)
        {//position in [mm]
            if (motors == null || motor < 1 || motor > 2)
                throw new ApplicationException("MoveMotorAbs: PI Motors not initialized or motor not=(1 or 2).");

            if (!motors[motor - 1].isHomed)
                throw new ApplicationException("MoveMotorAbs: Motor not homed prior to requesting move, motor=" + motor.ToString());

            double[] dVals = new double[1];
            dVals[0] = position;
            if (dVals[0] < motors[motor - 1].minLimit || dVals[0] > motors[motor - 1].maxLimit)
                throw new ArgumentOutOfRangeException("MoveMotorAbs: requested move exceeds motor limits, motor=" + motor.ToString() + ", current position=" + motors[motor - 1].setPosition.ToString() + " and requested position=" + position.ToString());

            PI.GCS2.MOV(motors[motor - 1].controllerID, motors[motor - 1].axisID, dVals);

            if (blocking)
            {
                int[] bFlags = new int[1];
                do
                {
                    System.Threading.Thread.Sleep(BlockingWait);
                    PI.GCS2.IsMoving(motors[motor - 1].controllerID, motors[motor - 1].axisID, bFlags);//is the motor moving?
                } while (bFlags[0] != 0); //wait until motor stops moving
            }

            motors[motor - 1].setPosition = position;
        }

        public void SetMotorLimits(int motor, double min, double max)
        {
            if (motors == null || motor < 1 || motor > 2)
                throw new ApplicationException("SetMotorLimits: PI Motors not initialized or motor not=(1 or 2).");

            if (!motors[motor - 1].isHomed)
                throw new ApplicationException("SetMotorLimits: Limits cannot be set prior to homing, motor=" + motor.ToString());

            if (min < MinPosition || max > MaxPosition || min >= max)
                throw new ArgumentOutOfRangeException("SetMotorLimits: Limits are outside valid range, min=" + min.ToString() + ", max=" + max.ToString());

            if (motors[motor - 1].setPosition < min || motors[motor - 1].setPosition > max)
                throw new ApplicationException("SetMotorLimits: motor must be positioned within set limits, actual=" + motors[motor - 1].setPosition.ToString() + ", min=" + min.ToString() + ", max=" + max.ToString());

            motors[motor - 1].minLimit = min;
            motors[motor - 1].maxLimit = max;
        }
    }
}
