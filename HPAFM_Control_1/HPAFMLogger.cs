using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HPAFM_Control_1
{
    class HPAFMLogger
    {
        public enum LogLevel { Info = 0, Warning = 1, Error = 2 };

        static string fnlog = System.IO.Directory.GetCurrentDirectory() + "\\LOG_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm") + ".txt";
        static string fndata = System.IO.Directory.GetCurrentDirectory() + "\\DATA_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm") + ".txt";

        static string[] lvls = { "INF - ", "WRN - ", "ERR - " };

        public static void LogMessage(LogLevel level, string message, bool showMessageBox = false)
        {
            StringBuilder msg = new StringBuilder(DateTime.Now.ToString("HH:mm:ss - "));
            msg.Append(lvls[(int)level]);
            msg.Append(message);
            msg.AppendLine();
            System.IO.File.AppendAllText(fnlog, msg.ToString());

            if (showMessageBox)
            {
                System.Windows.MessageBox.Show(msg.ToString());
            }
        }

        public static string OutputFileSelect(string defaultName)
        {
            Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog();
            dlg.DefaultExt = ".xml";
            dlg.FileName = defaultName;
            dlg.Filter = "XML Files (.xml)|*.xml";
            dlg.InitialDirectory = Properties.Settings.Default.DataOutDir;

            if (dlg.ShowDialog() == true)
            {
                fndata = dlg.FileName;
                fnlog = fndata + ".log";
                Properties.Settings.Default.DataOutDir = System.IO.Path.GetDirectoryName(fndata);
            }
            else
            {
                return null;
            }

            return fndata;
        }

        public static void WriteHeader(string experiment, string sample, string probe)
        {
            StringBuilder msg = new StringBuilder("<?xml version='1.0'?>");
            msg.AppendLine();
            msg.Append("<fdout version='1' experiment='");
            msg.Append(experiment);
            msg.Append("' sample='");
            msg.Append(sample);
            msg.Append("' probe='");
            msg.Append(probe);
            msg.Append("' start='");
            msg.Append(DateTime.Now.ToString());
            msg.Append("'>");
            msg.AppendLine();
            System.IO.File.AppendAllText(fndata, msg.ToString());
        }

        public static void WriteFooter()
        {
            System.IO.File.AppendAllText(fndata, "</fdout>");
        }

        public static void WriteFDHeader(double sampleLoc, double probeLoc, double pressure, double temperature, int num)
        {
            StringBuilder msg = new StringBuilder("<fd x='");
            msg.Append(sampleLoc);
            msg.Append(" mm' z='");
            msg.Append(probeLoc);
            msg.Append(" mm' Pmeas='");
            msg.Append(pressure);
            msg.Append("' Tmeas='");
            msg.Append(temperature);
            msg.Append("' num='");
            msg.Append(num);
            msg.Append("' start='");
            msg.Append(DateTime.Now.ToString());
            msg.Append("'>");
            msg.AppendLine();
            System.IO.File.AppendAllText(fndata, msg.ToString());
        }

        public static void WriteFDPoints(List<InterfaceDataBox.FDPoint> points)
        {
            StringBuilder msg = new StringBuilder();
            for (int i = 0; i < points.Count; i++)
            {
                msg.Append(points[i].voltage);
                msg.Append(',');
                msg.Append(points[i].pll_ampl);
                msg.Append(',');
            }
            msg.AppendLine();
            System.IO.File.AppendAllText(fndata, msg.ToString());
        }

        public static void WriteFDPoints2(double[] t, double[] a)
        {
            StringBuilder msg = new StringBuilder();
            for (int i = 0; i < t.Length; i++)
            {
                msg.Append(t[i]);
                msg.Append(',');
                msg.Append(a[i]);
                msg.Append(',');
            }
            msg.AppendLine();
            System.IO.File.AppendAllText(fndata, msg.ToString());
        }

        public static void WriteFDPoints3(double[] a)
        {
            StringBuilder msg = new StringBuilder();
            for (int i = 0; i < a.Length; i++)
            {
                msg.Append(a[i]);
                msg.Append(',');
            }
            msg.AppendLine();
            string fn = System.IO.Directory.GetCurrentDirectory() + "\\FD_" + DateTime.Now.ToString("HH-mm") + ".txt";
            System.IO.File.AppendAllText(fn, msg.ToString());
        }

        public static void WriteFDPoints4(double a)
        {
            StringBuilder msg = new StringBuilder();
            msg.Append(a);
            msg.AppendLine();
            System.IO.File.AppendAllText(fndata, msg.ToString());
        }

        public static void WriteFDPoints5(string a)
        {
            StringBuilder msg = new StringBuilder();
            msg.Append(a);
            msg.AppendLine();
            System.IO.File.AppendAllText(fndata, msg.ToString());
        }

        public static void AnalyzeFDPoints(List<InterfaceDataBox.FDPoint> points)
        {
            int icontact = -1; //index of contact and release points, if any - if none then remains -1
            int irelease = -1;
            int contact_lim = 620;//falling lower than this implies we have switched to touching
            int release_lim = 740;//rising above this implies we have released from touching
            bool isTouching = false;//start off with an assumption of non-touching

            for(int i=0; i<points.Count; i++)
            {
                if (isTouching)
                {
                    if (points[i].pll_ampl > release_lim)
                    {
                        irelease = i;
                        break;
                    }
                }
                else
                {
                    if(points[i].pll_ampl < contact_lim)
                    {
                        icontact = i;
                        isTouching = true;
                    }
                }
            }

            StringBuilder msg = new StringBuilder();

            if (irelease != -1)
            {//found contact and release sections - potentially valid FD
                msg.Append("Found contact and release points indices ");
                msg.Append(icontact);
                msg.Append(',');
                msg.Append(irelease);
                msg.Append(" and voltages ");
                msg.Append(points[icontact].voltage);
                msg.Append(',');
                msg.Append(points[irelease].voltage);
                msg.Append(" dV=");
                msg.Append(points[irelease].voltage - points[icontact].voltage);
            }
            else
            {
                if(icontact != -1)
                {//found contact but no release - too sticky? or some other error
                    msg.Append("Found contact but NOT release points indices ");
                    msg.Append(icontact);
                    msg.Append(" and voltages ");
                    msg.Append(points[icontact].voltage);
                }
                else
                {//did not find contact or release - not touching?
                    msg.Append("No contact or release points");
                }
            }

            msg.AppendLine();
            System.IO.File.AppendAllText(fndata, msg.ToString());

        }

        public static void WriteFDFooter()
        {
            System.IO.File.AppendAllText(fndata, "</fd>\r\n");
        }

        public static void WritePiezoCalHeader(double pressure, double temperature, int num)
        {
            StringBuilder msg = new StringBuilder("<piezocal Pmeas='");
            msg.Append(pressure);
            msg.Append("' Tmeas='");
            msg.Append(temperature);
            msg.Append("' num='");
            msg.Append(num);
            msg.Append("' start='");
            msg.Append(DateTime.Now.ToString());
            msg.Append("'>");
            msg.AppendLine();
            System.IO.File.AppendAllText(fndata, msg.ToString());
        }

        public static void WritePiezoCalPoints(List<InterfaceDataBox.PiezoCalPoint> points)
        {
            StringBuilder msg = new StringBuilder();
            for (int i = 0; i < points.Count; i++)
            {
                msg.Append(points[i].voltage);
                msg.Append(',');
                msg.Append(points[i].ldv_piezo);
                msg.Append(',');
                msg.Append(points[i].ldv_chamber);
                msg.Append(',');
            }
            msg.AppendLine();
            System.IO.File.AppendAllText(fndata, msg.ToString());
        }

        public static void WritePiezoCalFooter()
        {
            System.IO.File.AppendAllText(fndata, "</piezocal>\r\n");
        }

        public static void WriteHCPoint(long time, int duty, double pwr, double temp_s, double temp_c, double temp_i)
        {
            StringBuilder msg = new StringBuilder();
            msg.Append(time);
            msg.Append(',');
            msg.Append(duty);
            msg.Append(',');
            msg.Append(pwr);
            msg.Append(',');
            msg.Append(temp_s);
            msg.Append(',');
            msg.Append(temp_c);
            msg.Append(',');
            msg.Append(temp_i);
            msg.AppendLine();
            System.IO.File.AppendAllText(fndata, msg.ToString());
        }
    }
}
