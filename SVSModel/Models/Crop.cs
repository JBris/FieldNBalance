﻿// FieldNBalance is a program that estimates the N balance and provides N fertilizer recommendations for cultivated crops.
// Author: Hamish Brown.
// Copyright (c) 2024 The New Zealand Institute for Plant and Food Research Limited

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Data.Analysis;
using SVSModel;
using SVSModel.Configuration;
using SVSModel.Simulation;

namespace SVSModel
{
    public class Crop
    {
        /// <summary>
        /// Returns daily N uptake over the duration of the Tt input data for Root, Stover, Product and loss N as well as cover and root depth
        /// </summary>
        /// <param name="tt">An array containing the accumulated thermal time for the duration of the crop</param>
        /// <param name="cf">A specific class that holds all the simulation configuration data in the correct types for use in the model</param>
        /// <returns>A 2D array of crop model outputs</returns>
        public static CropType Grow(Dictionary<DateTime, double> tt,
                                     CropConfig cf)
        {
            CropType thisCrop = new CropType();
            ///Set up data structures
            thisCrop.growDates = Functions.DateSeries(cf.EstablishDate, cf.HarvestDate);
            DataFrame allCropParams = Crop.LoadCropCoefficients();
            CropParams cropParams = ExtractCropParams(cf.CropNameFull, allCropParams);// new Dictionary<string, double>();

            // Derive Crop Parameters
            thisCrop.Tt_Harv = tt.Values.Last();
            thisCrop.Tt_estab = thisCrop.Tt_Harv * (Constants.PropnTt[cf.EstablishStage] / Constants.PropnTt[cf.HarvestStage]);
            thisCrop.Xo_Biomass = (thisCrop.Tt_Harv + thisCrop.Tt_estab) * .45 * (1 / Constants.PropnTt[cf.HarvestStage]);
            thisCrop.b_Biomass = thisCrop.Xo_Biomass * .25;
            thisCrop.T_mat = thisCrop.Xo_Biomass * 2.2222;
            thisCrop.T_maxRD = Constants.PropnTt["EarlyReproductive"] * thisCrop.T_mat;
            thisCrop.T_sen = Constants.PropnTt["MidReproductive"] * thisCrop.T_mat;
            thisCrop.Xo_cov = thisCrop.Xo_Biomass * 0.4 / cropParams.rCover;
            thisCrop.b_cov = thisCrop.Xo_cov * 0.2;
            thisCrop.typicalYield = cropParams.TypicalYield * Constants.UnitConversions[cropParams.TypicalYieldUnits];
            thisCrop.a_harvestIndex = cropParams.TypicalHI - cropParams.HIRange;
            thisCrop.b_harvestIndex = cropParams.HIRange / thisCrop.typicalYield;
            thisCrop.stageCorrection = 1 / Constants.PropnMaxDM[cf.HarvestStage];

            // derive crop Harvest State Variables 
            thisCrop.fFieldLossPct = cf.FieldLoss;
            thisCrop.fTotalProductFwt = cf.FieldYield * (1 / (1 - thisCrop.fFieldLossPct / 100));
            thisCrop.HI = thisCrop.a_harvestIndex + thisCrop.fTotalProductFwt * thisCrop.b_harvestIndex;
            if (cropParams.YieldType == "Standing DM")
            {
                thisCrop.fTotalProductFwt *= thisCrop.HI; // Yield is input at total standing DM but then partitioned to product and stover so need to adjust down her so it is only product
            }
            thisCrop.fTotalProductDwt = thisCrop.fTotalProductFwt * (1 - cf.MoistureContent / 100);
            thisCrop.fFieldLossDwt = thisCrop.fTotalProductDwt * thisCrop.fFieldLossPct / 100;
            thisCrop.fFieldLossN = thisCrop.fFieldLossDwt * cropParams.ProductN / 100;
            thisCrop.fSaleableProductDwt = thisCrop.fTotalProductDwt - thisCrop.fFieldLossDwt - thisCrop.fDressingLossDwt;
            thisCrop.fSaleableProductN = thisCrop.fSaleableProductDwt * cropParams.ProductN / 100;
            thisCrop.fStoverDwt = thisCrop.fTotalProductDwt * 1 / thisCrop.HI - thisCrop.fTotalProductDwt;
            thisCrop.fStoverN = thisCrop.fStoverDwt * cropParams.StoverN / 100;
            thisCrop.fRootDwt = (thisCrop.fStoverDwt + thisCrop.fTotalProductDwt) * cropParams.PRoot;
            thisCrop.fRootN = thisCrop.fRootDwt * cropParams.RootN / 100;
            thisCrop.fCropN = thisCrop.fRootN + thisCrop.fStoverN + thisCrop.fFieldLossN + thisCrop.fDressingLossN + thisCrop.fSaleableProductN;
            thisCrop.nHIRoot = thisCrop.fRootN / thisCrop.fCropN;
            thisCrop.nHIStover = thisCrop.fStoverN / thisCrop.fCropN;
            thisCrop.nHIFieldLoss = thisCrop.fFieldLossN  / thisCrop.fCropN;



            //Daily time-step, calculate Daily Scallers to give in-crop patterns
            Dictionary<DateTime, double> biomassScaller = new Dictionary<DateTime, double>();
            Dictionary<DateTime, double> coverScaller = new Dictionary<DateTime, double>();
            Dictionary<DateTime, double> rootDepthScaller = new Dictionary<DateTime, double>();
            foreach (DateTime d in tt.Keys)
            {
                double bmScaller = (1 / (1 + Math.Exp(-((tt[d] - thisCrop.Xo_Biomass) / (thisCrop.b_Biomass)))));
                biomassScaller.Add(d, bmScaller);
                double rdScaller = 1;
                if (tt[d] < thisCrop.T_maxRD)
                    rdScaller = tt[d] / thisCrop.T_maxRD;
                rootDepthScaller.Add(d, rdScaller);
                double cScaller = Math.Max(0, (1 - (tt[d] - thisCrop.T_sen) / (thisCrop.T_mat - thisCrop.T_sen)));
                if (tt[d] < thisCrop.T_sen)
                    cScaller = 1 / (1 + Math.Exp(-((tt[d] - thisCrop.Xo_cov) / thisCrop.b_cov)));
                coverScaller.Add(d, cScaller);
            }

            // Multiply Harvest State Variables by Daily Scallers to give Daily State Variables
            thisCrop.RootN = Functions.scaledValues(biomassScaller, thisCrop.fRootN, thisCrop.stageCorrection);
            thisCrop.StoverN = Functions.scaledValues(biomassScaller, thisCrop.fStoverN, thisCrop.stageCorrection);
            thisCrop.SaleableProductN = Functions.scaledValues(biomassScaller, thisCrop.fSaleableProductN, thisCrop.stageCorrection);
            thisCrop.FieldLossN = Functions.scaledValues(biomassScaller, thisCrop.fFieldLossN, thisCrop.stageCorrection);
            thisCrop.DressingLossN = Functions.scaledValues(biomassScaller, thisCrop.fDressingLossN, thisCrop.stageCorrection);
            thisCrop.TotalCropN = Functions.scaledValues(biomassScaller, thisCrop.fCropN, thisCrop.stageCorrection);
            thisCrop.CropUptakeN = Functions.dictMaker(thisCrop.growDates, Functions.calcDelta(thisCrop.TotalCropN.Values.ToArray()));
            thisCrop.Cover = Functions.scaledValues(coverScaller, cropParams.Acover, 1.0);
            thisCrop.RootDepth = Functions.scaledValues(rootDepthScaller, cropParams.MaxRD, 1.0);


            
            return thisCrop;
        }

        public static DataFrame LoadCropCoefficients()
        {
            string resourceName = "SVSModel.Data.CropCoefficientTableFull.csv";
            var assembly = Assembly.GetExecutingAssembly();
            Stream csv = assembly.GetManifestResourceStream(resourceName);
            DataFrame allCropCoeffs = DataFrame.LoadCsv(csv);
            return allCropCoeffs;
        }

        public static CropParams ExtractCropParams(string crop, DataFrame allCropParams)
        {
            int cropRow = 0;
            bool cropNotFound = true;
            while (cropNotFound)
            {
                if (allCropParams[cropRow, 0].ToString() == crop)
                    cropNotFound = false;
                else
                    cropRow += 1;
            }

            List<string> coeffs = new List<string> { "Typical Yield","Typical Yield Units","Yield type","Typical Population (/ha)",
                                                      "TotalOrDry","Typical Dressing Loss %","Typical Field Loss %","Typical HI",
                                                      "HI Range","Moisture %","P Root","Max RD","A cover","rCover","Root [N]",
                                                      "Stover [N]","Product [N]" };

            Dictionary<string, object> cropParamDict = new Dictionary<string, object>();
            foreach (string c in coeffs)
            {
                cropParamDict.Add(c, allCropParams[c][cropRow]);
            }

            CropParams cropParams = new CropParams(cropParamDict);
            return cropParams;
        }

        public static void ConstrainNUptake(ref SimulationType thisSim, double nShortage, DateTime shortageDate)
         {
            CropConfig current = null;
            if ((shortageDate >= thisSim.config.Current.EstablishDate) && (shortageDate <= thisSim.config.Current.HarvestDate))
                current = thisSim.config.Current;
            else if ((shortageDate >= thisSim.config.Following.EstablishDate) && (shortageDate <= thisSim.config.Following.HarvestDate))
                current = thisSim.config.Following;
            thisSim.NUptake[shortageDate] -= nShortage;
            thisSim.ExportN[current.HarvestDate.AddDays(1)] -= nShortage;
            DateTime[] constrainDates = Functions.DateSeries(shortageDate, current.HarvestDate);
            foreach (DateTime d in constrainDates)
            {
                thisSim.CropN[d] -= nShortage;
            }
            current.ResRoot -= nShortage * current.SimResults.nHIRoot;
            current.ResStover -= nShortage * current.SimResults.nHIStover; ;
            current.ResFieldLoss -= nShortage * current.SimResults.nHIFieldLoss;
            current.NUptake -= nShortage;
        }
    }
    public class CropType
    {

        public DateTime[] growDates;
        ///Crop parameters
        public double Tt_Harv;
        public double Tt_estab;
        public double Xo_Biomass;
        public double b_Biomass;
        public double T_mat;
        public double T_maxRD;
        public double T_sen;
        public double Xo_cov;
        public double b_cov;
        public double typicalYield;
        public double a_harvestIndex;
        public double b_harvestIndex;
        public double stageCorrection;
        public double fFieldLossPct;
        public double fTotalProductFwt;
        public double fTotalProductDwt;
        public double fFieldLossDwt;
        public double fFieldLossN;
        public double fDressingLossDwt;
        public double fDressingLossN;
        public double fSaleableProductDwt;
        public double fSaleableProductN;
        public double HI;
        public double fStoverDwt;
        public double fStoverN;
        public double fRootDwt;
        public double fRootN;
        public double fCropN;
        public double nHIRoot;
        public double nHIStover;
        public double nHIFieldLoss;

        /// Crop daily variables
        
        public Dictionary<DateTime, double> RootN;
        public Dictionary<DateTime, double> StoverN;
        public Dictionary<DateTime, double> SaleableProductN;
        public Dictionary<DateTime, double> FieldLossN;
        public Dictionary<DateTime, double> DressingLossN;
        public Dictionary<DateTime, double> TotalCropN;
        public Dictionary<DateTime, double> CropUptakeN;
        public Dictionary<DateTime, double> Cover;
        public Dictionary<DateTime, double> RootDepth;
        public Dictionary<DateTime, double> TotalNDemand;

    }
}


