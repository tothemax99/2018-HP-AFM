using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO.Ports;
using System.Threading;

namespace HPAFM_Control_1
{
    public class InterfaceHPHardware
    {
        //com port of temperature/HP controller defined in project settings
        const int HPID = 25826034; //ID of temperature/HP controller
        const int MaxTemperature = 315; //maximum temperature setpoint in degC
        const int SerialWait = 20; //wait time in ms to get a response from controller
        const byte Rmask = 0b0111_1111;
        const byte Tmask = 0b1000_0000;
        SerialPort HPPort = null;
        byte[] serialBuf = new byte[30]; //serial transmit/receive buffer

        public float HeaterPower { get { return heater_power; } }
        public float TempWater { get { return temp_water; } }
        public float PressureWater { get { return pressure_water; } }
        public float TempSurface { get { return temp_surface; } }
        public float TempController { get { return temp_controller; } }
        public bool EmergencySwitchPressed { get { return sw1; } }
        public bool ChamberValvesOpen { get { return sw2; } }
        public byte HeaterDutyCycle { get { return heater_duty; } }
        public ThermocoupleError TempWaterError { get { return tw_err; } }
        public ThermocoupleError TempSurfaceError { get { return ts_err; } }
        public HeaterState HeaterStatus { get { return heater_status; } }

        float heater_power = 0, temp_water = 0, temp_surface = 0, temp_controller = 0, pressure_water = 0;
        bool sw1 = true, sw2 = true;
        byte heater_duty = 0;
        ThermocoupleError tw_err = ThermocoupleError.OpenCircuit, ts_err = ThermocoupleError.OpenCircuit;
        HeaterState heater_status = HeaterState.Off;

        //variables list, contains the ID used in serial communication, ID must be <128 to leave MSB as control bit
        //0=ID (unique identifier) int, 1*=light pattern, 2=valves and thermocouple errors, 3*=heater duty cycle
        //4=heater measure power (W), 5=water temp (C), 6=surface temp (C), 7=controller temp (C), 8=water pressure (PSI)
        //public enum VarsFloat { HeaterPower = 4, WaterTemp = 5, SurfaceTemp = 6, ControllerTemp = 7, WaterPressure = 8 };

        public enum ThermocoupleError { OK, OpenCircuit, ShortGND, ShortVcc };
        public enum HeaterState { Off, HeatUp, CoolDown, Stabilize, Stable };

        public void InitializeHPController()
        {
            if (HPPort != null)
                throw new ApplicationException("HPBox cannot be initialized as it is already initialized.");

            try
            {
                HPPort = new SerialPort(Properties.Settings.Default.HPBoxPort, 115200, Parity.None, 8, StopBits.One);//baud rate isn't really used by Teensy driver
                HPPort.Open();
            }
            catch (Exception e)
            {
                HPPort = null;
                throw new ApplicationException("HPBox port could not be opened.", e);
            }

            sendVarRequest(0); //ask controller ID
            Thread.Sleep(SerialWait); //wait to receive data

            if (HPPort.BytesToRead < 5)
            { //expected response: "[ID][0x12][0x34][0x56][0x78]"
                Exit();
                throw new ApplicationException("HPBox too few bytes responding to ID request.");
            }
            HPPort.Read(serialBuf, 0, 5);
            if (serialBuf[0] != (0 | Tmask))//expected byte indicating a write-out from device
            {
                Exit();
                throw new ApplicationException("HPBox incorrect byte responding to ID request.");
            }

            int id = BitConverter.ToInt32(serialBuf, 1);

            if (id != HPID)
            { //not correct ID
                Exit();
                throw new ApplicationException("HPBox incorrect ID value in ID request.");
            }

            //response OK means we are connected to controller
        }

        private void sendVarRequest(int varID)
        {
            serialBuf[0] = (byte)((byte)varID & Rmask); //set MSB to zero, indicating read
            HPPort.Write(serialBuf, 0, 1); //send request
        }

        /*private void sendVarValue(int varID, int value)
        {
            serialBuf[0] = (byte)((byte)varID | Tmask); //set MSB to one, indicating write
            byte[] val = BitConverter.GetBytes(value);
            serialBuf[1] = val[0];
            serialBuf[2] = val[1];
            serialBuf[3] = val[2];
            serialBuf[4] = val[3];
            HPPort.Write(serialBuf, 0, 5); //send value
        }

        private void sendVarValue(VarsFloat varID, float value)
        {
            serialBuf[0] = (byte)((byte)varID | Tmask); //set MSB to one, indicating write
            byte[] val = BitConverter.GetBytes(value);
            serialBuf[1] = val[0];
            serialBuf[2] = val[1];
            serialBuf[3] = val[2];
            serialBuf[4] = val[3];
            HPPort.Write(serialBuf, 0, 5); //send value
        }

        private bool loadReceivedVar(int varID, out int value)
        {
            value = 0;

            if (HPPort.BytesToRead < 5)
            { //expected response: "[ID][0x12][0x34][0x56][0x78]"
                return false;
            }

            HPPort.Read(serialBuf, 0, 5);

            if(serialBuf[0] != ((byte)varID | Tmask))//expected byte indicating a write-out from device
            {
                return false;
            }

            value = BitConverter.ToInt32(serialBuf, 1);

            return true;
        }*/

        public bool GetIllumStatus()
        {//is illumination LED on or off?
            if (HPPort == null)
                throw new ApplicationException("GetIllumStatus: HPBox port is not initialized, cannot continue");

            sendVarRequest(1); //request status
            Thread.Sleep(SerialWait); //wait to receive data
            if (HPPort.BytesToRead < 2)
            { //expected response: 2 bytes [ID] 0x0A where A=0 or 1
                throw new ApplicationException("GetIllumStatus: not enough bytes in response, BytesToRead = " + HPPort.BytesToRead.ToString());
            }

            HPPort.Read(serialBuf, 0, 2);

            if (serialBuf[0] != (1 | Tmask))//expected byte indicating a write-out from device
            {
                throw new ApplicationException("GetIllumStatus: incorrect first byte value = " + serialBuf[0].ToString());
            }

            return serialBuf[1] != 0;
        }

        public void UpdateMeasurements()
        {//updates all measured values
            if (HPPort == null)
                throw new ApplicationException("UpdateMeasurements: HPBox port is not initialized, cannot continue");

            sendVarRequest(10);  //request everything          
            Thread.Sleep(SerialWait); //wait to receive data

            if (HPPort.BytesToRead < 25)
            { //expected response: 25 bytes [ID] (heater state)-(temp err)-(valve sw)-(heater duty)-(heater power)()()()-(temp water)()()()-(temp surface)()()()-(temp controller)()()()-(pressure)()()()
                throw new ApplicationException("UpdateMeasurements: not enough bytes in response, BytesToRead = " + HPPort.BytesToRead.ToString());
            }

            HPPort.Read(serialBuf, 0, 25);

            if (serialBuf[0] != (10 | Tmask))//expected byte indicating a write-out from device
            {
                throw new ApplicationException("UpdateMeasurements: incorrect first byte value = " + serialBuf[0].ToString());
            }

            if (serialBuf[1] == 0)
                heater_status = HeaterState.Off;
            if (serialBuf[1] == 1)
                heater_status = HeaterState.HeatUp;
            if (serialBuf[1] == 2)
                heater_status = HeaterState.CoolDown;
            if (serialBuf[1] == 3)
                heater_status = HeaterState.Stabilize;
            if (serialBuf[1] == 4)
                heater_status = HeaterState.Stable;

            //error of thermocouples in water,surface: 0b(abcd)(abcd)  a=error b=open c=short_GND d=short_Vcc
            if ((serialBuf[2] & 0b1000_0000) == 0)
                tw_err = ThermocoupleError.OK;
            if ((serialBuf[2] & 0b0100_0000) != 0)
                tw_err = ThermocoupleError.OpenCircuit;
            if ((serialBuf[2] & 0b0010_0000) != 0)
                tw_err = ThermocoupleError.ShortGND;
            if ((serialBuf[2] & 0b0001_0000) != 0)
                tw_err = ThermocoupleError.ShortVcc;

            if ((serialBuf[2] & 0b0000_1000) == 0)
                ts_err = ThermocoupleError.OK;
            if ((serialBuf[2] & 0b0000_0100) != 0)
                ts_err = ThermocoupleError.OpenCircuit;
            if ((serialBuf[2] & 0b0000_0010) != 0)
                ts_err = ThermocoupleError.ShortGND;
            if ((serialBuf[2] & 0b0000_0001) != 0)
                ts_err = ThermocoupleError.ShortVcc;

            //values of attached valve switches: 0b0000 00ba  a=switch 1 (emergency stop), b=switch 2 (chamber valves)
            sw1 = (serialBuf[3] & 0x01) != 0;
            sw2 = (serialBuf[3] & 0x02) != 0;

            heater_duty = serialBuf[4]; //desired heater duty cycle, set by PC

            heater_power = BitConverter.ToSingle(serialBuf, 5);
            temp_water = BitConverter.ToSingle(serialBuf, 9);
            temp_surface = BitConverter.ToSingle(serialBuf, 13);
            temp_controller = BitConverter.ToSingle(serialBuf, 17);
            pressure_water = BitConverter.ToSingle(serialBuf, 21);
        }

        public float GetTargetWaterTemp()
        {
            if (HPPort == null)
                throw new ApplicationException("GetTargetWaterTemp: HPBox port is not initialized, cannot continue");

            sendVarRequest(5);  //request temp target          
            Thread.Sleep(SerialWait); //wait to receive data

            if (HPPort.BytesToRead < 5)
            { //expected response: 5 bytes [ID] 
                throw new ApplicationException("GetTargetWaterTemp: not enough bytes in response, BytesToRead = " + HPPort.BytesToRead.ToString());
            }

            HPPort.Read(serialBuf, 0, 5);

            if (serialBuf[0] != (5 | Tmask))//expected byte indicating a write-out from device
            {
                throw new ApplicationException("GetTargetWaterTemp: incorrect first byte value = " + serialBuf[0].ToString());
            }

            return BitConverter.ToSingle(serialBuf, 1);
        }

        public void SetIllumination(bool illuminationLED)
        {
            if (HPPort == null)
                throw new ApplicationException("SetIllumination: HPBox port is not initialized, cannot continue");

            serialBuf[0] = (1 | Tmask); //set MSB to one, indicating write
            serialBuf[1] = 0;
            serialBuf[2] = 0;
            serialBuf[3] = 0;
            serialBuf[4] = (byte)(illuminationLED ? 1 : 0);
            HPPort.Write(serialBuf, 0, 5); //send value
        }

        /*public void SetHeaterDutyCycle(byte duty)
        {
            if (HPPort == null)
                throw new ApplicationException("SetHeaterDutyCycle: HPBox port is not initialized, cannot continue");

            serialBuf[0] = (3 | Tmask); //set MSB to one, indicating write
            serialBuf[1] = 0;
            serialBuf[2] = 0;
            serialBuf[3] = 0;
            serialBuf[4] = duty;
            HPPort.Write(serialBuf, 0, 5); //send value
        }*/

        public void SetTargetWaterTemp(float temp)
        {
            if (HPPort == null)
                throw new ApplicationException("SetTargetWaterTemp: HPBox port is not initialized, cannot continue");

            serialBuf[0] = (5 | Tmask); //set MSB to one, indicating write
            byte[] val = BitConverter.GetBytes(temp);
            serialBuf[1] = val[0];
            serialBuf[2] = val[1];
            serialBuf[3] = val[2];
            serialBuf[4] = val[3];
            HPPort.Write(serialBuf, 0, 5); //send value
        }

        public void TurnOffHeater()
        {
            if (HPPort == null)
                throw new ApplicationException("TurnOffHeater: HPBox port is not initialized, cannot continue");

            serialBuf[0] = (4 | Tmask); //set MSB to one, indicating write
            serialBuf[1] = 0;
            serialBuf[2] = 0;
            serialBuf[3] = 0;
            serialBuf[4] = 0;
            HPPort.Write(serialBuf, 0, 5); //send value
        }
        
        public void Exit()
        {
            if (HPPort == null)
                return;

            //turn off heater with a low setpoint
            //SetHeaterDutyCycle(0); not needed as controller will turn off heater by itself after no comms

            HPPort.Close();
            HPPort = null;
        }
    }
}
