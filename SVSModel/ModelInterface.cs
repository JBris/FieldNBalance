﻿// FieldNBalance is a program that estimates the N balance and provides N fertilizer recommendations for cultivated crops.
// Author: Hamish Brown.
// Copyright (c) 2024 The New Zealand Institute for Plant and Food Research Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using CsvHelper;
using SVSModel.Configuration;
using SVSModel.Models;
using SVSModel.Simulation;

namespace SVSModel
{
    public interface IModelInterface
    {
        /// <summary>
        /// User friendly interface for calling SimulateField
        /// </summary>
        /// <param name="weatherStation">A string representing the closest weather station 'gore' | 'hastings' | 'levin' | 'lincoln' | 'pukekohe'</param>
        /// <param name="testResults">A dictionary of nitrogen test results</param>
        /// <param name="nApplied">A dictionary of nitrogen applications</param>
        /// <param name="config">Model config object, all parameters are required</param>
        /// <returns>A list of <see cref="DailyNBalance"/> objects</returns>
        List<DailyNBalance> GetDailyNBalance(string weatherStation, Dictionary<DateTime, double> testResults, Dictionary<DateTime, double> nApplied, Config config, double initialN);
        
        /// <summary>
        /// Gets the crop data from the data file
        /// </summary>
        /// <returns>List of <see cref="CropCoefficient"/>s directly from the data file</returns>
        IEnumerable<CropCoefficient> GetCropCoefficients();

    }

    public class ModelInterface : IModelInterface
    {
        public List<DailyNBalance> GetDailyNBalance(string weatherStation, Dictionary<DateTime, double> testResults, Dictionary<DateTime, double> nApplied, Config config, double initialN)
        {
            var startDate = config.Prior.EstablishDate.AddDays(-1);
            var endDate = config.Following.HarvestDate.AddDays(2);
            var metData = BuildMetDataDictionaries(startDate, endDate, weatherStation, false);

            var rawResult = Simulation.Simulation.SimulateField(metData.MeanT, metData.Rain, metData.MeanPET, testResults, nApplied, config, Constants.InitialN);

            var result = new List<DailyNBalance>();

            // Convert from the 2d object array that SimulateField returns into something user friendly
            for (var r = 1; r < rawResult.GetLength(0); r++)
            {
                var row = Enumerable.Range(0, rawResult.GetLength(1))
                    .Select(x => rawResult[r, x])
                    .ToList();

                var values = row.Skip(1).OfType<double>().ToArray();

                var data = new DailyNBalance
                {
                    Date = (DateTime)row[0],
                    SoilMineralN = values[0],
                    UptakeN = values[1],
                    ResidueN = values[2],
                    SoilOMN = values[3],
                    FertiliserN = values[4],
                    CropN = values[5],
                    ProductN = values[6],
                    LostN = values[7],
                    RSWC = values[8],
                    Drainage = values[9],
                    Irrigation = values[10],
                    GreenCover = values[11],
                };

                result.Add(data);
            }

            return result;
        }

        public IEnumerable<CropCoefficient> GetCropCoefficients()
        {
            var assembly = Assembly.GetExecutingAssembly();

            var stream = assembly.GetManifestResourceStream("SVSModel.Data.CropCoefficientTableFull.csv");
            if (stream == null) return Enumerable.Empty<CropCoefficient>();

            using (var reader = new StreamReader(stream, Encoding.UTF8))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                csv.Context.RegisterClassMap<CropCoefficientMap>();

                var cropData = csv.GetRecords<CropCoefficient>();
                return cropData.ToList();
            }
        }

        private static IEnumerable<WeatherStationData> GetMetData(string weatherStation)
        {
            var assembly = Assembly.GetExecutingAssembly();

            var stream = assembly.GetManifestResourceStream($"SVSModel.Data.Met.{weatherStation}.csv");
            if (stream == null) return Enumerable.Empty<WeatherStationData>();

            using (var reader = new StreamReader(stream, Encoding.UTF8))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                var data = csv.GetRecords<WeatherStationData>();
                return data.ToList();
            }
        }
        private static IEnumerable<TestStationData> GetActualMetData(string weatherStation)
        {
            var assembly = Assembly.GetExecutingAssembly();

            var stream = assembly.GetManifestResourceStream($"SVSModel.Data.Met.{weatherStation}.csv");
            if (stream == null) return Enumerable.Empty<TestStationData>();

            using (var reader = new StreamReader(stream, Encoding.UTF8))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                var data = csv.GetRecords<TestStationData>();
                return data.ToList();
            }
        }

        public static MetDataDictionaries BuildMetDataDictionaries(DateTime startDate, DateTime endDate, string weatherStation, bool actualWeather)
        {
            var meanT = new Dictionary<DateTime, double>();
            var rain = new Dictionary<DateTime, double>();
            var meanPET = new Dictionary<DateTime, double>();

            if (actualWeather)
            {
                var metData = GetActualMetData(weatherStation).ToList();
                var currDate = new DateTime(startDate.Year, startDate.Month, startDate.Day);
                while (currDate < endDate)
                {
                    var doy = currDate.DayOfYear;
                    var year = currDate.Year;
                    foreach (TestStationData t in metData)
                    {
                        if ((t.Year == year) && (t.DOY == doy))
                        {
                            meanT.Add(currDate, t?.MeanT ?? 0);
                            rain.Add(currDate, t?.Rain ?? 0);
                            meanPET.Add(currDate, t?.MeanPET ?? 0);
                            break;
                        }
                    }
                    currDate = currDate.AddDays(1);
                }
            }
            else
            {
                var metData = GetMetData(weatherStation).ToList();
                var currDate = new DateTime(startDate.Year, startDate.Month, startDate.Day);
                while (currDate < endDate)
                {
                    var doy = currDate.DayOfYear;
                    var values = metData.FirstOrDefault(m => m.DOY == doy);

                    meanT.Add(currDate, values?.MeanT ?? 0);
                    rain.Add(currDate, values?.Rain ?? 0);
                    meanPET.Add(currDate, values?.MeanPET ?? 0);

                    currDate = currDate.AddDays(1);
                }
            }
            return new MetDataDictionaries { MeanT = meanT, Rain = rain, MeanPET = meanPET };
        }
    }
}
