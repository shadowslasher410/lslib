using System.Runtime.CompilerServices;

namespace LSLib.LS;

public partial class Matrix(int rows, int cols)
{
    public int Rows { get; } = rows;
    public int Cols { get; } = cols;

    public double[] Mat { get; } = new double[rows * cols];

    public Matrix? L { get; set; }
    public Matrix? U { get; set; }

    private int[]? _pi;
    private double _detOfP = 1;

    public bool IsSquare() => Rows == Cols;

    public double this[int iRow, int iCol]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Mat[iRow * Cols + iCol];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Mat[iRow * Cols + iCol] = value;
    }

    public Matrix GetCol(int k)
    {
        Matrix m = new(Rows, 1);
        var dest = m.Mat.AsSpan();
        var source = Mat.AsSpan();

        for (int i = 0; i < Rows; i++)
            dest[i] = source[i * Cols + k];

        return m;
    }

    public void SetCol(Matrix v, int k)
    {
        var dest = Mat.AsSpan();
        var source = v.Mat.AsSpan();

        for (int i = 0; i < Rows; i++)
            dest[i * Cols + k] = source[i];
    }

    public void MakeLU()
    {
        if (!IsSquare()) throw new MatrixException("The matrix is not square!");

        L = IdentityMatrix(Rows, Cols);
        U = Duplicate();

        _pi = new int[Rows];
        Span<int> piSpan = _pi.AsSpan();
        for (int i = 0; i < Rows; i++) piSpan[i] = i;

        Span<double> lMat = L.Mat.AsSpan();
        Span<double> uMat = U.Mat.AsSpan();

        int k0 = 0;

        for (int k = 0; k < Cols - 1; k++)
        {
            double p = 0;

            for (int i = k; i < Rows; i++)
            {
                double val = Math.Abs(uMat[i * Cols + k]);
                if (val > p)
                {
                    p = val;
                    k0 = i;
                }
            }

            if (p == 0)
                throw new MatrixException("The matrix is singular!");

            (piSpan[k], piSpan[k0]) = (piSpan[k0], piSpan[k]);

            if (k != k0)
            {
                int rowKOffset = k * Cols;
                int rowK0Offset = k0 * Cols;

                for (int j = 0; j < k; j++)
                {
                    (lMat[rowKOffset + j], lMat[rowK0Offset + j]) = (lMat[rowK0Offset + j], lMat[rowKOffset + j]);
                }

                _detOfP *= -1;

                for (int j = 0; j < Cols; j++)
                {
                    (uMat[rowKOffset + j], uMat[rowK0Offset + j]) = (uMat[rowK0Offset + j], uMat[rowKOffset + j]);
                }
            }

            int currentPivotIdx = k * Cols + k;
            double pivotValue = uMat[currentPivotIdx];

            for (int i = k + 1; i < Rows; i++)
            {
                int iOffset = i * Cols;
                double factor = uMat[iOffset + k] / pivotValue;
                lMat[iOffset + k] = factor;

                int targetRowOffset = i * Cols;
                int sourceRowOffset = k * Cols;

                for (int j = k; j < Cols; j++)
                {
                    uMat[targetRowOffset + j] -= factor * uMat[sourceRowOffset + j];
                }
            }
        }
    }



    public Matrix SolveWith(Matrix v)
    {
        if (Rows != Cols) throw new MatrixException("The matrix is not square!");
        if (Rows != v.Rows) throw new MatrixException("Wrong number of results in solution vector!");

        if (L is null) MakeLU();

        Matrix b = new(Rows, 1);

        Span<double> bMat = b.Mat.AsSpan();
        ReadOnlySpan<double> vMat = v.Mat.AsSpan();
        ReadOnlySpan<int> piSpan = _pi.AsSpan();

        for (int i = 0; i < Rows; i++)
        {
            bMat[i] = vMat[piSpan[i]];
        }

        Matrix z = SubsForth(L!, b);
        Matrix x = SubsBack(U!, z);

        return x;
    }


    public Matrix Duplicate()
    {
        Matrix matrix = new(Rows, Cols);

        Mat.AsSpan().CopyTo(matrix.Mat.AsSpan());

        return matrix;
    }


    public static Matrix SubsForth(Matrix A, Matrix b)
    {
        int n = A.Rows;
        Matrix x = new(n, 1);

        ReadOnlySpan<double> aMat = A.Mat.AsSpan();
        ReadOnlySpan<double> bMat = b.Mat.AsSpan();
        Span<double> xMat = x.Mat.AsSpan();

        int aCols = A.Cols;

        for (int i = 0; i < n; i++)
        {
            double sum = bMat[i];
            int rowOffset = i * aCols;

            for (int j = 0; j < i; j++)
            {
                sum -= aMat[rowOffset + j] * xMat[j];
            }

            xMat[i] = sum / aMat[rowOffset + i];
        }
        return x;
    }

    public static Matrix SubsBack(Matrix A, Matrix b)
    {
        int n = A.Rows;
        Matrix x = new(n, 1);

        ReadOnlySpan<double> aMat = A.Mat.AsSpan();
        ReadOnlySpan<double> bMat = b.Mat.AsSpan();
        Span<double> xMat = x.Mat.AsSpan();

        int aCols = A.Cols;

        for (int i = n - 1; i >= 0; i--)
        {
            double sum = bMat[i];
            int rowOffset = i * aCols;

            for (int j = n - 1; j > i; j--)
            {
                sum -= aMat[rowOffset + j] * xMat[j];
            }

            xMat[i] = sum / aMat[rowOffset + i];
        }
        return x;
    }

    public static Matrix ZeroMatrix(int iRows, int iCols)
    {
        return new Matrix(iRows, iCols);
    }

    public static Matrix IdentityMatrix(int iRows, int iCols)
    {
        Matrix matrix = new(iRows, iCols);
        Span<double> matSpan = matrix.Mat.AsSpan();

        int minDim = Math.Min(iRows, iCols);

        int stride = iCols + 1;
        for (int i = 0; i < minDim; i++)
        {
            matSpan[i * stride] = 1.0;
        }

        return matrix;
    }


    public static Matrix Parse(string ps)
    {
        if (string.IsNullOrWhiteSpace(ps))
            throw new MatrixException("Input string cannot be null or empty.");

        string s = NormalizeMatrixString(ps);

        ReadOnlySpan<char> span = s.AsSpan();

        int rowCount = span.Count("\r\n") + 1;

        int firstNewlineIdx = span.IndexOf("\r\n");
        ReadOnlySpan<char> firstRowSpan = firstNewlineIdx == -1 ? span : span[..firstNewlineIdx];
        int colCount = firstRowSpan.Count(" ") + 1;

        Matrix matrix = new(rowCount, colCount);
        Span<double> matSpan = matrix.Mat.AsSpan();

        int rowIndex = 0;

        foreach (var rowRange in span.Split("\r\n"))
        {
            ReadOnlySpan<char> rowSpan = span[rowRange];
            int colIndex = 0;

            foreach (var numRange in rowSpan.Split(' '))
            {
                ReadOnlySpan<char> numSpan = rowSpan[numRange];

                if (colIndex >= colCount || rowIndex >= rowCount)
                    throw new MatrixException("Matrix row/column shape mismatch.");

                if (!double.TryParse(numSpan, null, out double value))
                    throw new MatrixException("Wrong input format!");

                matSpan[rowIndex * colCount + colIndex] = value;
                colIndex++;
            }

            rowIndex++;
        }

        return matrix;
    }


    private static void SafeAplusBintoC(Matrix A, int xa, int ya, Matrix B, int xb, int yb, Matrix C, int size)
    {
        int aRows = A.Rows, aCols = A.Cols;
        int bRows = B.Rows, bCols = B.Cols;
        int cCols = C.Cols;

        ReadOnlySpan<double> aMat = A.Mat.AsSpan();
        ReadOnlySpan<double> bMat = B.Mat.AsSpan();
        Span<double> cMat = C.Mat.AsSpan();

        for (int i = 0; i < size; i++)
        {
            int cRowOffset = i * cCols;
            int aRowIndex = ya + i;
            int bRowIndex = yb + i;

            bool aRowValid = aRowIndex < aRows;
            bool bRowValid = bRowIndex < bRows;

            int aRowOffset = aRowIndex * aCols;
            int bRowOffset = bRowIndex * bCols;

            for (int j = 0; j < size; j++)
            {
                double val = 0;

                if (aRowValid && (xa + j < aCols))
                    val += aMat[aRowOffset + xa + j];

                if (bRowValid && (xb + j < bCols))
                    val += bMat[bRowOffset + xb + j];

                cMat[cRowOffset + j] = val;
            }
        }
    }

    private static void SafeAminusBintoC(Matrix A, int xa, int ya, Matrix B, int xb, int yb, Matrix C, int size)
    {
        int aRows = A.Rows, aCols = A.Cols;
        int bRows = B.Rows, bCols = B.Cols;
        int cCols = C.Cols;

        ReadOnlySpan<double> aMat = A.Mat.AsSpan();
        ReadOnlySpan<double> bMat = B.Mat.AsSpan();
        Span<double> cMat = C.Mat.AsSpan();

        for (int i = 0; i < size; i++)
        {
            int cRowOffset = i * cCols;
            int aRowIndex = ya + i;
            int bRowIndex = yb + i;

            bool aRowValid = aRowIndex < aRows;
            bool bRowValid = bRowIndex < bRows;

            int aRowOffset = aRowIndex * aCols;
            int bRowOffset = bRowIndex * bCols;

            for (int j = 0; j < size; j++)
            {
                double val = 0;

                if (aRowValid && (xa + j < aCols))
                    val += aMat[aRowOffset + xa + j];

                if (bRowValid && (xb + j < bCols))
                    val -= bMat[bRowOffset + xb + j];

                cMat[cRowOffset + j] = val;
            }
        }
    }

    private static void SafeACopytoC(Matrix A, int xa, int ya, Matrix C, int size)
    {
        int aRows = A.Rows, aCols = A.Cols;
        int cCols = C.Cols;

        ReadOnlySpan<double> aMat = A.Mat.AsSpan();
        Span<double> cMat = C.Mat.AsSpan();

        for (int i = 0; i < size; i++)
        {
            int cRowOffset = i * cCols;
            int aRowIndex = ya + i;
            bool aRowValid = aRowIndex < aRows;
            int aRowOffset = aRowIndex * aCols;

            for (int j = 0; j < size; j++)
            {
                double val = 0;

                if (aRowValid && (xa + j < aCols))
                    val = aMat[aRowOffset + xa + j];

                cMat[cRowOffset + j] = val;
            }
        }
    }

    private static void AplusBintoC(Matrix A, int xa, int ya, Matrix B, int xb, int yb, Matrix C, int size)
    {
        int aCols = A.Cols, bCols = B.Cols, cCols = C.Cols;

        ReadOnlySpan<double> aMat = A.Mat.AsSpan();
        ReadOnlySpan<double> bMat = B.Mat.AsSpan();
        Span<double> cMat = C.Mat.AsSpan();

        for (int i = 0; i < size; i++)
        {
            int aRowOffset = (ya + i) * aCols + xa;
            int bRowOffset = (yb + i) * bCols + xb;
            int cRowOffset = i * cCols;

            for (int j = 0; j < size; j++)
            {
                cMat[cRowOffset + j] = aMat[aRowOffset + j] + bMat[bRowOffset + j];
            }
        }
    }

    private static void AminusBintoC(Matrix A, int xa, int ya, Matrix B, int xb, int yb, Matrix C, int size)
    {
        int aCols = A.Cols, bCols = B.Cols, cCols = C.Cols;

        ReadOnlySpan<double> aMat = A.Mat.AsSpan();
        ReadOnlySpan<double> bMat = B.Mat.AsSpan();
        Span<double> cMat = C.Mat.AsSpan();

        for (int i = 0; i < size; i++)
        {
            int aRowOffset = (ya + i) * aCols + xa;
            int bRowOffset = (yb + i) * bCols + xb;
            int cRowOffset = i * cCols;

            for (int j = 0; j < size; j++)
            {
                cMat[cRowOffset + j] = aMat[aRowOffset + j] - bMat[bRowOffset + j];
            }
        }
    }

    private static void ACopytoC(Matrix A, int xa, int ya, Matrix C, int size)
    {
        int aCols = A.Cols, cCols = C.Cols;

        ReadOnlySpan<double> aMat = A.Mat.AsSpan();
        Span<double> cMat = C.Mat.AsSpan();

        for (int i = 0; i < size; i++)
        {
            int aRowOffset = (ya + i) * aCols + xa;
            int cRowOffset = i * cCols;

            ReadOnlySpan<double> sourceRowSlice = aMat.Slice(aRowOffset, size);
            Span<double> destRowSlice = cMat.Slice(cRowOffset, size);

            sourceRowSlice.CopyTo(destRowSlice);
        }
    }

    private static Matrix StrassenMultiply(Matrix A, Matrix B)
    {
        if (A.Cols != B.Rows) throw new MatrixException("Wrong dimension of matrix!");

        int aRows = A.Rows, aCols = A.Cols;
        int bRows = B.Rows, bCols = B.Cols;

        int msize = Math.Max(Math.Max(aRows, aCols), Math.Max(bRows, bCols));

        if (msize < 32)
        {
            Matrix baseResult = ZeroMatrix(aRows, bCols);
            int baseCols = baseResult.Cols;

            ReadOnlySpan<double> aMat = A.Mat.AsSpan();
            ReadOnlySpan<double> bMat = B.Mat.AsSpan();
            Span<double> baseMat = baseResult.Mat.AsSpan();

            for (int i = 0; i < aRows; i++)
            {
                int aRowOffset = i * aCols;
                int rRowOffset = i * baseCols;

                for (int k = 0; k < aCols; k++)
                {
                    double aVal = aMat[aRowOffset + k];
                    int bRowOffset = k * bCols;

                    for (int j = 0; j < bCols; j++)
                    {
                        baseMat[rRowOffset + j] += aVal * bMat[bRowOffset + j];
                    }
                }
            }
            return baseResult;
        }

        int size = 1; int n = 0;
        while (msize > size) { size *= 2; n++; }
        int h = size / 2;

        Matrix[,] mField = new Matrix[n + 1, 9];

        int z;
        for (int i = 0; i < n - 4; i++)
        {
            z = (int)Math.Pow(2, n - i - 1);
            for (int j = 0; j < 9; j++) mField[i, j] = new Matrix(z, z);
        }

        SafeAplusBintoC(A, 0, 0, A, h, h, mField[0, 0], h);
        SafeAplusBintoC(B, 0, 0, B, h, h, mField[0, 1], h);
        StrassenMultiplyRun(mField[0, 0], mField[0, 1], mField[0, 1 + 1], 1, mField);

        SafeAplusBintoC(A, 0, h, A, h, h, mField[0, 0], h);
        SafeACopytoC(B, 0, 0, mField[0, 1], h);
        StrassenMultiplyRun(mField[0, 0], mField[0, 1], mField[0, 1 + 2], 1, mField);

        SafeACopytoC(A, 0, 0, mField[0, 0], h);
        SafeAminusBintoC(B, h, 0, B, h, h, mField[0, 1], h);
        StrassenMultiplyRun(mField[0, 0], mField[0, 1], mField[0, 1 + 3], 1, mField);

        SafeACopytoC(A, h, h, mField[0, 0], h);
        SafeAminusBintoC(B, 0, h, B, 0, 0, mField[0, 1], h);
        StrassenMultiplyRun(mField[0, 0], mField[0, 1], mField[0, 1 + 4], 1, mField);

        SafeAplusBintoC(A, 0, 0, A, h, 0, mField[0, 0], h);
        SafeACopytoC(B, h, h, mField[0, 1], h);
        StrassenMultiplyRun(mField[0, 0], mField[0, 1], mField[0, 1 + 5], 1, mField);

        SafeAminusBintoC(A, 0, h, A, 0, 0, mField[0, 0], h);
        SafeAplusBintoC(B, 0, 0, B, h, 0, mField[0, 1], h);
        StrassenMultiplyRun(mField[0, 0], mField[0, 1], mField[0, 1 + 6], 1, mField);

        SafeAminusBintoC(A, h, 0, A, h, h, mField[0, 0], h);
        SafeAplusBintoC(B, 0, h, B, h, h, mField[0, 1], h);
        StrassenMultiplyRun(mField[0, 0], mField[0, 1], mField[0, 1 + 7], 1, mField);

        Matrix R = new(aRows, bCols);
        Span<double> rMat = R.Mat.AsSpan();
        int rCols = R.Cols;

        ReadOnlySpan<double> m1 = mField[0, 1 + 1].Mat.AsSpan(); int fCols = mField[0, 1 + 1].Cols;
        ReadOnlySpan<double> m2 = mField[0, 1 + 2].Mat.AsSpan();
        ReadOnlySpan<double> m3 = mField[0, 1 + 3].Mat.AsSpan();
        ReadOnlySpan<double> m4 = mField[0, 1 + 4].Mat.AsSpan();
        ReadOnlySpan<double> m5 = mField[0, 1 + 5].Mat.AsSpan();
        ReadOnlySpan<double> m6 = mField[0, 1 + 6].Mat.AsSpan();
        ReadOnlySpan<double> m7 = mField[0, 1 + 7].Mat.AsSpan();

        int limitRowsH = Math.Min(h, aRows);
        int limitColsH = Math.Min(h, bCols);
        int limitRows2H = Math.Min(2 * h, aRows);
        int limitCols2H = Math.Min(2 * h, bCols);

        for (int i = 0; i < limitRows2H; i++)
        {
            int rRowOffset = i * rCols;
            bool isTopHalf = i < limitRowsH;
            int subRowIdx = isTopHalf ? i : i - h;
            int fRowOffset = subRowIdx * fCols;

            for (int j = 0; j < limitCols2H; j++)
            {
                bool isLeftHalf = j < limitColsH;
                int subColIdx = isLeftHalf ? j : j - h;
                int fIdx = fRowOffset + subColIdx;

                if (isTopHalf)
                {
                    if (isLeftHalf)
                        rMat[rRowOffset + j] = m1[fIdx] + m4[fIdx] - m5[fIdx] + m7[fIdx];
                    else
                        rMat[rRowOffset + j] = m3[fIdx] + m5[fIdx];
                }
                else
                {
                    if (isLeftHalf)
                        rMat[rRowOffset + j] = m2[fIdx] + m4[fIdx];
                    else
                        rMat[rRowOffset + j] = m1[fIdx] - m2[fIdx] + m3[fIdx] + m6[fIdx];
                }
            }
        }

        return R;
    }

    private static void StrassenMultiplyRun(Matrix A, Matrix B, Matrix C, int l, Matrix[,] f)
    {
        int size = A.Rows;
        int h = size / 2;

        if (size < 32)
        {
            int cCols = C.Cols;
            int aCols = A.Cols;
            int bCols = B.Cols;

            ReadOnlySpan<double> aMat = A.Mat.AsSpan();
            ReadOnlySpan<double> bMat = B.Mat.AsSpan();
            Span<double> cMat = C.Mat.AsSpan();

            cMat.Clear();

            for (int i = 0; i < C.Rows; i++)
            {
                int aRowOffset = i * aCols;
                int cRowOffset = i * cCols;

                for (int k = 0; k < aCols; k++)
                {
                    double aVal = aMat[aRowOffset + k];
                    int bRowOffset = k * bCols;

                    for (int j = 0; j < bCols; j++)
                    {
                        cMat[cRowOffset + j] += aVal * bMat[bRowOffset + j];
                    }
                }
            }
            return;
        }

        AplusBintoC(A, 0, 0, A, h, h, f[l, 0], h);
        AplusBintoC(B, 0, 0, B, h, h, f[l, 1], h);
        StrassenMultiplyRun(f[l, 0], f[l, 1], f[l, 1 + 1], l + 1, f); // M1 = (A11 + A22) * (B11 + B22)

        AplusBintoC(A, 0, h, A, h, h, f[l, 0], h);
        ACopytoC(B, 0, 0, f[l, 1], h);
        StrassenMultiplyRun(f[l, 0], f[l, 1], f[l, 1 + 2], l + 1, f); // M2 = (A21 + A22) * B11

        ACopytoC(A, 0, 0, f[l, 0], h);
        AminusBintoC(B, h, 0, B, h, h, f[l, 1], h);
        StrassenMultiplyRun(f[l, 0], f[l, 1], f[l, 1 + 3], l + 1, f); // M3 = A11 * (B12 - B22)

        ACopytoC(A, h, h, f[l, 0], h);
        AminusBintoC(B, 0, h, B, 0, 0, f[l, 1], h);
        StrassenMultiplyRun(f[l, 0], f[l, 1], f[l, 1 + 4], l + 1, f); // M4 = A22 * (B21 - B11)

        AplusBintoC(A, 0, 0, A, h, 0, f[l, 0], h);
        ACopytoC(B, h, h, f[l, 1], h);
        StrassenMultiplyRun(f[l, 0], f[l, 1], f[l, 1 + 5], l + 1, f); // M5 = (A11 + A12) * B22

        AminusBintoC(A, 0, h, A, 0, 0, f[l, 0], h);
        AplusBintoC(B, 0, 0, B, h, 0, f[l, 1], h);
        StrassenMultiplyRun(f[l, 0], f[l, 1], f[l, 1 + 6], l + 1, f); // M6 = (A21 - A11) * (B11 + B12)

        AminusBintoC(A, h, 0, A, h, h, f[l, 0], h);
        AplusBintoC(B, 0, h, B, h, h, f[l, 1], h);
        StrassenMultiplyRun(f[l, 0], f[l, 1], f[l, 1 + 7], l + 1, f); // M7 = (A12 - A22) * (B21 + B22)

        Span<double> cMatTarget = C.Mat.AsSpan();
        int cColsStride = C.Cols;

        ReadOnlySpan<double> m1 = f[l, 1 + 1].Mat.AsSpan(); int fCols = f[l, 1 + 1].Cols;
        ReadOnlySpan<double> m2 = f[l, 1 + 2].Mat.AsSpan();
        ReadOnlySpan<double> m3 = f[l, 1 + 3].Mat.AsSpan();
        ReadOnlySpan<double> m4 = f[l, 1 + 4].Mat.AsSpan();
        ReadOnlySpan<double> m5 = f[l, 1 + 5].Mat.AsSpan();
        ReadOnlySpan<double> m6 = f[l, 1 + 6].Mat.AsSpan();
        ReadOnlySpan<double> m7 = f[l, 1 + 7].Mat.AsSpan();

        for (int i = 0; i < size; i++)
        {
            int cRowOffset = i * cColsStride;
            bool isTopHalf = i < h;
            int subRowIdx = isTopHalf ? i : i - h;
            int fRowOffset = subRowIdx * fCols;

            for (int j = 0; j < size; j++)
            {
                bool isLeftHalf = j < h;
                int subColIdx = isLeftHalf ? j : j - h;
                int fIdx = fRowOffset + subColIdx;

                if (isTopHalf)
                {
                    if (isLeftHalf) // C11
                        cMatTarget[cRowOffset + j] = m1[fIdx] + m4[fIdx] - m5[fIdx] + m7[fIdx];
                    else // C12
                        cMatTarget[cRowOffset + j] = m3[fIdx] + m5[fIdx];
                }
                else
                {
                    if (isLeftHalf) // C21
                        cMatTarget[cRowOffset + j] = m2[fIdx] + m4[fIdx];
                    else // C22
                        cMatTarget[cRowOffset + j] = m1[fIdx] - m2[fIdx] + m3[fIdx] + m6[fIdx];
                }
            }
        }
    }

    private static Matrix Multiply(double n, Matrix m)
    {
        Matrix r = new(m.Rows, m.Cols);

        ReadOnlySpan<double> mSpan = m.Mat.AsSpan();
        Span<double> rSpan = r.Mat.AsSpan();

        for (int i = 0; i < mSpan.Length; i++)
        {
            rSpan[i] = mSpan[i] * n;
        }

        return r;
    }

    private static Matrix Add(Matrix m1, Matrix m2)
    {
        if (m1.Rows != m2.Rows || m1.Cols != m2.Cols)
            throw new MatrixException("Matrices must have the same dimensions!");

        Matrix r = new(m1.Rows, m1.Cols);

        ReadOnlySpan<double> m1Span = m1.Mat.AsSpan();
        ReadOnlySpan<double> m2Span = m2.Mat.AsSpan();
        Span<double> rSpan = r.Mat.AsSpan();

        for (int i = 0; i < m1Span.Length; i++)
        {
            rSpan[i] = m1Span[i] + m2Span[i];
        }

        return r;
    }

    private static Matrix Subtract(Matrix m1, Matrix m2)
    {
        if (m1.Rows != m2.Rows || m1.Cols != m2.Cols)
            throw new MatrixException("Matrices must have the same dimensions!");

        Matrix r = new(m1.Rows, m1.Cols);

        ReadOnlySpan<double> m1Span = m1.Mat.AsSpan();
        ReadOnlySpan<double> m2Span = m2.Mat.AsSpan();
        Span<double> rSpan = r.Mat.AsSpan();

        for (int i = 0; i < m1Span.Length; i++)
        {
            rSpan[i] = m1Span[i] - m2Span[i];
        }

        return r;
    }

    public static string NormalizeMatrixString(string matStr)
    {
        if (string.IsNullOrWhiteSpace(matStr)) return string.Empty;

        ReadOnlySpan<char> source = matStr.AsSpan().Trim();

        char[] rentedBuffer = System.Buffers.ArrayPool<char>.Shared.Rent(source.Length);
        Span<char> destination = rentedBuffer.AsSpan();

        int destIdx = 0;
        bool inWhitespace = false;

        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];

            if (c is ' ' or '\t')
            {
                if (!inWhitespace)
                {
                    destination[destIdx++] = ' ';
                    inWhitespace = true;
                }
            }
            else if (c is '\r' or '\n')
            {
                if (destIdx > 0 && destination[destIdx - 1] == ' ')
                {
                    destIdx--;
                }

                destination[destIdx++] = '\r';
                destination[destIdx++] = '\n';

                if (c == '\r' && i + 1 < source.Length && source[i + 1] == '\n')
                {
                    i++;
                }

                while (i + 1 < source.Length && source[i + 1] is ' ' or '\t' or '\r' or '\n')
                {
                    i++;
                    if (source[i] is '\r' or '\n')
                    {
                        if (source[i] == '\r' && i + 1 < source.Length && source[i + 1] == '\n') i++;
                    }
                }

                inWhitespace = false;
            }
            else
            {
                destination[destIdx++] = c;
                inWhitespace = false;
            }
        }

        ReadOnlySpan<char> resultSpan = destination[..destIdx].Trim();
        string finalString = resultSpan.ToString();

        System.Buffers.ArrayPool<char>.Shared.Return(rentedBuffer);

        return finalString;
    }

    public static Matrix operator -(Matrix m) => Multiply(-1, m);
    public static Matrix operator +(Matrix m1, Matrix m2) => Add(m1, m2);
    public static Matrix operator -(Matrix m1, Matrix m2) => Subtract(m1, m2);
    public static Matrix operator *(Matrix m1, Matrix m2) => StrassenMultiply(m1, m2);
    public static Matrix operator *(double n, Matrix m) => Multiply(n, m);
    public static Matrix operator *(Matrix m, double n) => Multiply(n, m);
    
}

public class MatrixException(string Message) : Exception(Message);