﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Globalization;
using ISAAR.MSolve.FEM.Interfaces;
using ISAAR.MSolve.Materials.Interfaces;

namespace ISAAR.MSolve.FEM.Stochastic
{
    public class GaussianPCFileStochasticCoefficientsProvider : IStochasticMaterialCoefficientsProvider, IPCCoefficientsProvider
    {
        private readonly StochasticCoefficientsMemoizer memoizer;
        private readonly int orderM, orderP;
        private readonly double[][,] evp;
        private readonly int[,] evpPos;
        private readonly double[] eigs;
        private readonly double[] translations;
        private readonly double mean;
        private readonly double covariance;
        private readonly double[] stochasticDomain;
        private readonly int[] varianceAxes = new int[] { 0, 1, 2 };
        private readonly IPolynomialChaosCoefficients calculator;
        private static NumberFormatInfo ni = null;

        public int OrderM { get { return orderM; } }
        public int OrderP { get { return orderP; } }
        public int CurrentOrder { get; set; }
        public int ExpansionOrder { get { return calculator.PsiSize - 1; } }
        public int NoOfMatrices { get { return orderM; } }
        public double[] RandomVariables { get; set; }
        public IPolynomialChaosCoefficients Calculator { get { return calculator; } }

        public GaussianPCFileStochasticCoefficientsProvider(string fileName, int noOfVariables, int orderM, int orderP, char separator, double[] stochasticDomain, string decimalSeparator = ".")
        {
            this.orderM = orderM;
            this.orderP = orderP;
            this.calculator = new PolynomialChaosCoefficientsCalculator(orderM, orderP, true);
            CultureInfo ci = System.Globalization.CultureInfo.InstalledUICulture;
            ni = (System.Globalization.NumberFormatInfo)ci.NumberFormat.Clone();
            ni.NumberDecimalSeparator = decimalSeparator;
            this.stochasticDomain = stochasticDomain;

            int noOfCoords = stochasticDomain.Length;
            eigs = new double[noOfVariables];
            evpPos = new int[noOfVariables, 3];
            evp = new double[noOfCoords][,];
            translations = new double[noOfCoords];

            for (int i = 0; i < noOfCoords; i++)
            {
                translations[i] = stochasticDomain[i] / 2;
                evp[i] = new double[noOfVariables, 3];
            }

            string[] lines = File.ReadAllLines(fileName);
            string[] values;
            for (int i = 0; i < noOfVariables; i++)
            {
                for (int j = 0; j < noOfCoords; j++)
                {
                    values = lines[i + j * noOfVariables].Split(separator);
                    evp[j][i, 0] = double.Parse(values[0], ni);
                    evp[j][i, 1] = double.Parse(values[1], ni);
                    evp[j][i, 2] = double.Parse(values[2], ni);
                }

                values = lines[i + noOfCoords * noOfVariables].Split(separator);
                evpPos[i, 0] = int.Parse(values[0], ni);
                evpPos[i, 1] = int.Parse(values[1], ni);
                evpPos[i, 2] = int.Parse(values[2], ni);

                values = lines[i + (noOfCoords + 1) * noOfVariables].Split(separator);
                eigs[i] = double.Parse(values[0], ni);
            }
            values = lines[(noOfCoords + 2) * noOfVariables].Split(separator);
            mean = double.Parse(values[0], ni);
            values = lines[(noOfCoords + 2) * noOfVariables + 1].Split(separator);
            //covariance = double.Parse(values[0], ni);
            covariance = 0.15;
        }

        public GaussianPCFileStochasticCoefficientsProvider(string fileName, int noOfVariables, int orderM, int orderP, char separator, double[] stochasticDomain, int[] varianceAxes, string decimalSeparator = ".")
            : this(fileName, noOfVariables, orderM, orderP, separator, stochasticDomain, decimalSeparator)
        {
            this.varianceAxes = varianceAxes;
        }

        public GaussianPCFileStochasticCoefficientsProvider(string fileName, int noOfVariables, int orderM, int orderP, char separator, double[] stochasticDomain, StochasticCoefficientsMemoizer memoizer, string decimalSeparator = ".")
            : this(fileName, noOfVariables, orderM, orderP, separator, stochasticDomain, decimalSeparator)
        {
            this.memoizer = memoizer;
        }

        private double GetBasisCoefficientInternal(double[] coordinates, int order)
        {
            if (order == -1) return 1;

            double f = 1;
            if (memoizer == null || memoizer.Exists(coordinates, order) == false)
            {
                f = covariance * Math.Sqrt(eigs[order]);
                for (int i = 0; i < varianceAxes.Length; i++)
                {
                    int index = evpPos[order, varianceAxes[i]];
                    double w = evp[i][index - 1, 0];
                    double l = evp[i][index - 1, 1];
                    double a = evp[i][index - 1, 2];
                    double x = coordinates[varianceAxes[i]] - translations[varianceAxes[i]];

                    f *= index % 2 == 0 ? a * Math.Sin(w * x) : a * Math.Cos(w * x);
                }
                if (memoizer != null)
                    memoizer.SetCoefficient(coordinates, order, f);
            }
            else
                f = memoizer.GetCoefficient(coordinates, order);
            
            return f;
        }

        private double GetCoefficientInternal(double[] coordinates, int order)
        {
            return GetBasisCoefficientInternal(coordinates, order);
            //if (order < 1) return 1;

            //double l_n = 1;
            //for (int i = 0; i < calculator.PsiBasis[order].Length; i++)
            //    if (calculator.PsiBasis[order][i] > 0)
            //        l_n *= Math.Pow(GetBasisCoefficientInternal(coordinates, order), calculator.PsiBasis[order][i]);
            //return l_n / calculator.PsiSquareNorm[order];
        }

        #region IStochasticCoefficientsProvider Members

        double IStochasticCoefficientsProvider.GetCoefficient(double meanValue, double[] coordinates)
        {
            return meanValue * GetCoefficientInternal(coordinates, CurrentOrder);
        }

        #endregion

        #region IStochasticMaterialCoefficientsProvider Members

        double IStochasticMaterialCoefficientsProvider.GetCoefficient(double meanValue, double[] coordinates)
        {
            double result = 0;
            for (int i = 0; i < RandomVariables.Length; i++)
                result += RandomVariables[i] * GetCoefficientInternal(coordinates, i);
            return Math.Exp(result);
        }
        
        #endregion
    }
}
