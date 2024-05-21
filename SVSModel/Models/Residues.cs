﻿// FieldNBalance is a program that estimates the N balance and provides N fertilizer recommendations for cultivated crops.
// Author: Hamish Brown.
// Copyright (c) 2024 The New Zealand Institute for Plant and Food Research Limited

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Data.Analysis;
using SVSModel.Configuration;
using SVSModel.Simulation;

namespace SVSModel.Models
{
    public class Residues
    {
        /// <summary>
        /// Calculates the daily nitrogen mineralised as a result of residue decomposition
        /// </summary>
        /// <param name="meanT">A date indexed dictionary of daily mean temperatures</param>
        /// <param name="rswc">A date indexed dictionary of daily releative soil water content</param>
        /// <returns>Date indexed series of daily N mineralised from residues</returns>
        public static void Mineralisation(ref SimulationType thisSim)
        {
            DateTime[] simDates = thisSim.simDates;
            Config config = thisSim.config;
            //Dictionary<DateTime, double> NResidues = Functions.dictMaker(simDates, new double[simDates.Length]);

            ///Set up each cohort of residue
            DataFrame allCropParams = Crop.LoadCropCoefficients();
            CropParams priorCropParams = Crop.ExtractCropParams(config.Prior.CropNameFull, allCropParams);
            residue PresRoot = new residue(config.Prior.ResRoot, priorCropParams.RootN, config.Prior.HarvestDate, thisSim);
            residue PresStover = new residue(config.Prior.ResStover, priorCropParams.StoverN, config.Prior.HarvestDate, thisSim);
            residue PresFieldLoss = new residue(config.Prior.ResFieldLoss, priorCropParams.ProductN, config.Prior.HarvestDate, thisSim);
            
            CropParams currentCropParams = Crop.ExtractCropParams(config.Current.CropNameFull, allCropParams);
            residue CresRoot = new residue(config.Current.ResRoot, currentCropParams.RootN, config.Current.HarvestDate, thisSim);
            residue CresStover = new residue(config.Current.ResStover, currentCropParams.StoverN, config.Current.HarvestDate, thisSim);
            residue CresFieldLoss = new residue(config.Current.ResFieldLoss, currentCropParams.ProductN, config.Current.HarvestDate, thisSim);

            List<residue> Residues = new List<residue> { PresRoot, PresStover, PresFieldLoss, CresRoot, CresStover, CresFieldLoss };
            double[] TotalNetYesterday = new double[] {0,0,0,0,0,0};
            foreach (DateTime d in simDates)
            {
                double[] TotalNetToday = new double[6];
                int resInd = 0;
                foreach (residue r in Residues)
                {
                    TotalNetToday[resInd] += r.NetMineralisation[d];
                    resInd += 1;
                }
                thisSim.NResidues[d] = TotalNetToday.Sum() - TotalNetYesterday.Sum();
                TotalNetYesterday = TotalNetToday;
            }
        }


    }

    public class residue
    {
        public static string name()
        {
            return typeof(residue).Name;
        }
        private double ANm { get; set; }
        private double ANi { get; set; } 
        private double Km { get; set; } 
        private double Ki { get; set; }
        
        public Dictionary<DateTime, double> NetMineralisation { get; set; }

        public residue(double amountN, double Nconc, DateTime additionDate, SimulationType thisSim)
        {
            double CNR = 40/Nconc;
            this.ANm = amountN * 81.614/100;
            this.ANi = amountN * (4.8701+(CNR * 2.43475))/100;
            this.Km = 0.041678 + (1.026182 - 0.041678) * Math.Exp(-0.123883 * CNR) ;
            this.Ki = 0.112333 + (1.026182 - 0.041678) * Math.Exp(-0.130226 * CNR) ; 
            this.NetMineralisation = Functions.dictMaker(thisSim.simDates, new double[thisSim.simDates.Length]);
            double sigmaFtm = 0;
            foreach (DateTime d in thisSim.simDates)
            {
                if (d >= additionDate.AddDays(1))
                {
                    double Ft = VanHoffQ10(thisSim.meanT[d]);
                    double Fm = linearSoilWater(thisSim.RSWC[d]);
                    sigmaFtm += (Ft * Fm);
                    double mineralisation = ANm * (1 - Math.Exp(-Km * sigmaFtm));
                    double imobilisation = ANi * (1 - Math.Exp(-Ki * sigmaFtm));
                    NetMineralisation[d] = mineralisation - imobilisation;
                }
            }
        }

        public static double VanHoffQ10(double temp)
        {
            return Math.Pow(2, ((temp - 30) / 10));
        }
        public static double linearSoilWater(double rwc)
        {
            return Math.Min(1, rwc * 2);
        }

    }
}
