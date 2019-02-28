using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HPAFM_Control_1
{
    class WaterPropsCalculator
    {
        public const double MPatoPSI = 145.03;
        public const double atmPressureMPa = 0.10142;

        //water saturation data from Knovel steam tables
        static readonly double[] saturation_TC = { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150, 160, 170, 180, 190, 200, 210, 220, 230, 240, 250, 260, 270, 280, 290, 300, 310, 320, 330, 340, 350, 360, 370, 373.946};
        static readonly double[] saturation_PMPa = { 0.0006112, 0.0012282, 0.0023392, 0.0042467, 0.0073844, 0.012351, 0.019946, 0.031201, 0.047415, 0.070182, 0.10142, 0.14338, 0.19867, 0.27026, 0.3615, 0.4761, 0.61814, 0.79205, 1.0026, 1.255, 1.5547, 1.9074, 2.3193, 2.7968, 3.3467, 3.9759, 4.6921, 5.5028, 6.4165, 7.4416, 8.5877, 9.8647, 11.284, 12.858, 14.6, 16.529, 18.666, 21.043, 22.064 };

        public static double GetMaxLiquidTempC(double pressureMPa_rel)
        {//returns max temp to maintain liquid at given pressure
            double pressureMPa_abs = pressureMPa_rel + atmPressureMPa;

            if(pressureMPa_abs<saturation_PMPa[0] || pressureMPa_abs > saturation_PMPa[saturation_PMPa.Length - 1])
            {
                throw new ArgumentException("GetMaxLiquidTempC: pressure is out of valid data range pressureMPa_abs=" + pressureMPa_abs.ToString());
            }

            int i = 1;
            while(pressureMPa_abs > saturation_PMPa[i])
            {
                i++;
            }

            //do linear interpolation
            double dp1 = saturation_PMPa[i] - saturation_PMPa[i - 1];
            double dp2 = pressureMPa_abs - saturation_PMPa[i - 1];
            double dt = saturation_TC[i] - saturation_TC[i - 1];

            return saturation_TC[i - 1] + dt * (dp2 / dp1);
        }

        public static double GetMinLiquidPresMPa_rel(double temperatureC)
        {//returns min pressure to maintain liquid at given temperature
            if (temperatureC < saturation_TC[0] || temperatureC > saturation_TC[saturation_TC.Length - 1])
            {
                throw new ArgumentException("GetMinLiquidPresMPa_abs: temperature is out of valid data range temperatureC=" + temperatureC.ToString());
            }

            int i = 1;
            while (temperatureC > saturation_TC[i])
            {
                i++;
            }

            //do linear interpolation
            double dt1 = saturation_TC[i] - saturation_TC[i - 1];
            double dt2 = temperatureC - saturation_TC[i - 1];
            double dp = saturation_PMPa[i] - saturation_PMPa[i - 1];

            return saturation_PMPa[i - 1] + dp * (dt2 / dt1) - atmPressureMPa;
        }

        public static bool CheckLiquid(double pressureMPa_rel, double temperatureC)
        {//return true=liquid at given p/t, false=not 100% liquid at given p/t
            return GetMaxLiquidTempC(pressureMPa_rel) >= temperatureC;
        }
    }
}
