using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace HPAFM_Control_1
{
    class HPAFMReader
    {
        static string fndata = null;
        static public Queue<HPAFMAction> loadedAutomation = null;

        public static double ProcessInputFile()
        {
            if (fndata == null)
            {
                throw new ApplicationException("ProcessInputFile: filename is null");
            }

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "ProcessInputFile: loading automation data from " + fndata);

            //process it
            XmlReader xml = XmlReader.Create(fndata);

            xml.Read(); //read xml declaration <?...?>
            xml.Read(); //read next node which should be <automation>
            if (xml.NodeType == XmlNodeType.Whitespace)
                xml.Read(); //skip a line \n if necessary
            if(xml.NodeType != XmlNodeType.Element || xml.Name != "automation" || xml.GetAttribute("version") != "1")
            {
                throw new ApplicationException("ProcessInputFile: not a valid automation file format");
            }

            double time=0; //add up seconds of time to execute actions in queue
			HPAFMAction ac;
            Queue<HPAFMAction> qu = new Queue<HPAFMAction>();

            while (xml.Read())
            {
                if(xml.NodeType != XmlNodeType.Element)
                { //skip over spaces, newlines, endelements, and comments, stop only for elements
                    continue;
                }

                switch (xml.Name)
                {
                    case "conditions":
                        double pressure_MPa = double.Parse(xml.GetAttribute("P"));
                        double temp_C = double.Parse(xml.GetAttribute("T"));
                        ac = new HPAFMAction();
                        ac.actionType = HPAFMAction.Action.PTSet;
                        if (pressure_MPa < 0 || pressure_MPa > 14)
                        {
                            throw new ArgumentOutOfRangeException("ProcessInputFile: PT param out of range: pressure_MPa=" + pressure_MPa.ToString());
                        }
                        if (temp_C < 20 || temp_C > 315)
                        {
                            throw new ArgumentOutOfRangeException("ProcessInputFile: PT param out of range: temp_C=" + temp_C.ToString());
                        }
                        if (!WaterPropsCalculator.CheckLiquid(pressure_MPa, temp_C))
                        {
                            throw new ArgumentOutOfRangeException("ProcessInputFile: PT param out of range: requested P,T combination will cause vapor to form");
                        }
                        ac.arg1 = pressure_MPa * 145.03; //convert MPa to PSI
                        ac.arg2 = temp_C; //already in C
                        qu.Enqueue(ac);
						time+=100;
                        break;
                    case "piezocal":
                        double startV = double.Parse(xml.GetAttribute("start"));
                        double midV = double.Parse(xml.GetAttribute("mid"));
                        int num = int.Parse(xml.GetAttribute("num"));
                        ac = new HPAFMAction();
                        ac.actionType = HPAFMAction.Action.PiezoCal;
                        if (startV < 0 || startV > 10)
                        {
                            throw new ArgumentOutOfRangeException("ProcessInputFile: PC param out of range: startV=" + startV.ToString());
                        }
                        if (midV < 0 || midV > 10)
                        {
                            throw new ArgumentOutOfRangeException("ProcessInputFile: PC param out of range: midV=" + midV.ToString());
                        }
                        if (num < 1 || num > 50)
                        {
                            throw new ArgumentOutOfRangeException("ProcessInputFile: PC param out of range: num=" + num.ToString());
                        }
                        ac.arg1 = startV;
                        ac.arg2 = midV;
                        ac.arg3 = num;
                        qu.Enqueue(ac);
                        time += num * 2.2;
                        break;
                    case "fd":
                        double x = double.Parse(xml.GetAttribute("x"));
                        string approach = xml.GetAttribute("approach");
                        double down = double.Parse(xml.GetAttribute("down"));
                        startV = double.Parse(xml.GetAttribute("start"));
                        midV = double.Parse(xml.GetAttribute("mid"));
                        num = int.Parse(xml.GetAttribute("num"));
                        ac = new HPAFMAction();
                        ac.actionType = HPAFMAction.Action.SampleLoc;//sampleloc must appear together with fdcurve to properly engage/withdraw
                        if (x < 0 || x > ControlMotors.sample_range)
                        {
                            throw new ArgumentOutOfRangeException("ProcessInputFile: fd x location out of range: x=" + x.ToString());
                        }
                        ac.arg1 = x;
                        if (down < 0 || down > 0.010)
                        {
                            throw new ArgumentOutOfRangeException("ProcessInputFile: fd down param out of range: down=" + down.ToString());
                        }
                        ac.arg2 = down;
                        switch (approach)
                        {
                            case "slow":
                                ac.arg3 = 1;
                                break;
                            case "fast":
                                ac.arg3 = 2;
                                break;
                            default:
                                throw new ArgumentException("ProcessInputFile: fd approach parameter is not known: approach=" + approach);
                        }

                        //check fd param errors before enqueue so in case of error both sampleloc and fd are not present
                        if (startV < 0 || startV > 10)
                        {
                            throw new ArgumentOutOfRangeException("ProcessInputFile: fd param out of range: startV=" + startV.ToString());
                        }
                        if (midV < 0 || midV > 10)
                        {
                            throw new ArgumentOutOfRangeException("ProcessInputFile: fd param out of range: midV=" + midV.ToString());
                        }
                        if (num < 1 || num > 50)
                        {
                            throw new ArgumentOutOfRangeException("ProcessInputFile: fd param out of range: num=" + num.ToString());
                        }

                        qu.Enqueue(ac);
                        ac = new HPAFMAction();
                        ac.actionType = HPAFMAction.Action.FDcurve;//sampleloc must appear together with fdcurve to properly engage/withdraw
                        ac.arg1 = startV;
                        ac.arg2 = midV;
                        ac.arg3 = num;
                        qu.Enqueue(ac);
						time+=num*2.2;
                        break;
                    default:
                        throw new ApplicationException("ProcessInputFile: encountered an unknown automation command: " + xml.Name);
                }
            }

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "ProcessInputFile: successfully loaded " + qu.Count.ToString() + " actions into queue");

            loadedAutomation = qu;
			
			return time;
        }

        public static string InputFileSelect(string defaultName)
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog
            {
                DefaultExt = ".xml",
                FileName = defaultName,
                Filter = "XML Files (.xml)|*.xml",
                InitialDirectory = Properties.Settings.Default.DataOutDir
            };

            if (dlg.ShowDialog() == true)
            {
                fndata = dlg.FileName;
                //Properties.Settings.Default.DataOutDir = System.IO.Path.GetDirectoryName(fndata);
            }
            else
            {
                return null;
            }

            return fndata;
        }
    }


    public struct HPAFMAction
    {
        public enum Action { FDcurve, SampleLoc, PiezoCal, PTSet };
        public Action actionType;
        public double arg1, arg2;
        public int arg3;
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            switch (actionType)
            {
                case Action.FDcurve:
                    sb.Append("[FD Curve (vstart, vmid, num)][");
                    sb.Append(arg1);
                    sb.Append(',');
                    sb.Append(arg2);
                    sb.Append(',');
                    sb.Append(arg3);
                    sb.Append(']');
                    break;
                case Action.PiezoCal:
                    sb.Append("[PiezoCal (vstart, vmid, num)][");
                    sb.Append(arg1);
                    sb.Append(',');
                    sb.Append(arg2);
                    sb.Append(',');
                    sb.Append(arg3);
                    sb.Append(']');
                    break;
                case Action.PTSet:
                    sb.Append("[PTSet (P, T)][");
                    sb.Append(arg1);
                    sb.Append(',');
                    sb.Append(arg2);
                    sb.Append(']');
                    break;
                case Action.SampleLoc:
                    sb.Append("[SampleLoc (x, down, approach)][");
                    sb.Append(arg1);
                    sb.Append(',');
                    sb.Append(arg2);
                    sb.Append(',');
                    switch (arg3)
                    {
                        case 1:
                            sb.Append("slow");
                            break;
                        case 2:
                            sb.Append("fast");
                            break;
                        default:
                            sb.Append("undefined");
                            break;
                    }
                    sb.Append(']');
                    break;
            }
            return sb.ToString();
        }
    }

}
