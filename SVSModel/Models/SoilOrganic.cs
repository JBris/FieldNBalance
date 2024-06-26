﻿// FieldNBalance is a program that estimates the N balance and provides N fertilizer recommendations for cultivated crops.
// Author: Hamish Brown.
// Copyright (c) 2024 The New Zealand Institute for Plant and Food Research Limited

using System;
using System.Collections.Generic;
using System.Linq;
using SVSModel.Configuration;
using SVSModel.Simulation;

namespace SVSModel.Models
{
    public class SoilOrganic
    {
        /// <summary>
        /// Calculates the daily nitrogen mineralised as a result of soil organic matter decomposition
        /// </summary>
        /// <param name="meanT">A date indexed dictionary of daily mean temperatures</param>
        /// /// <param name="rswc">A date indexed dictionary of daily releative soil water content</param>
        /// <returns>Date indexed series of daily N mineralised from residues</returns>
        public static void Mineralisation(ref SimulationType thisSim)
        {
            DateTime[] simDates = thisSim.simDates;
            Config config = thisSim.config;
            double depthfactor = 30 * config.Field.SampleDepthFactor * (1-config.Field.Rocks); //Assumes all mineralisation happens in the top 30 cm but has an adjustment if sample only taken to 15 cm
            double pmn_mgPerg = config.Field.PMN;
            double pmn_kgPerha = pmn_mgPerg * config.Field.BulkDensity * depthfactor * 0.1;


            foreach (DateTime d in simDates)
            {
                double tempF = LloydTaylorTemp(thisSim.meanT[d]);
                double waterF = QiuBeareCurtinWater(thisSim.RSWC[d]);
                double somMin = pmn_kgPerha / 98 * tempF * waterF;
                thisSim.NSoilOM[d] = somMin;
            }
        }

        public static double LloydTaylorTemp(double t)
        {
            double ldt = 0.3124 * Math.Exp(308.56 * (1 / 56.02 - (1 / ((t + 273.15) - 227.13))));
            return ldt;
        }

        public static double QiuBeareCurtinWater(double rwc)
        {
            double swcr = 0.57 * Math.Pow(rwc, 2) + (0.15 * rwc) + 0.33;
            return Math.Min(swcr, 1.0);
        }
    }
}
