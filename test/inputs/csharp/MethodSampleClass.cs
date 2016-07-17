﻿using System;
using System.Diagnostics;

public static class MethodSampleClass
{
    public static void Trivial()
    {
    }

    public static int IntAdd(int a, int b)
    {
        return a + b;
    }

    public static int IntMax(int a, int b)
    {
        if (a > b)
        {
            return a;
        }
        else
        {
            return b;
        }
    }

    public static int IntAddNested(int a, int b, int c)
    {
        a = b + c + c;
        int d = b;
        b = c + IntAdd(a, b);
        a = b = c + IntAdd(a, b);
        d = d + d;
        int e = b, f;
        e = d;

        return IntAdd(a + c, b) + c;
    }

    public static int IntOperationsExample(int a, int b, int c)
    {
        int d = a + b;
        d = -a;
        d = -(a + c);
        d = a - c;
        d = a / c;
        d = a % c;
        d = a * c;

        d = a * b + c / 2;
        d = a * (b + c) / 2;
        return d;
    }

    public static bool BoolOperationsExample(bool a, bool b, bool c)
    {
        bool d = (a && b);
        d = (a & b);
        d = (a || b);
        d = (a | b);
        d = (a == b);
        d = (a != b);
        d = !c;

        d = a & b | c;
        d = a && b || c;
        d = a && (b || c);

        return a;
    }

    public static bool IntBoolOperationsExample(int a, int b, bool c)
    {
        bool d = (a == b);
        d = (a != b);
        d = (a > b);
        d = (a < b);
        d = (a >= b);
        d = (a <= b);

        d = ((a > b) || c);

        if ((a > b) && (a != b || c))
        {
            return c;
        }

        return !c;
    }

    public static int LogicExample(int a, int b, int c, bool condition)
    {
        if ((a == 0 || 10 / b > 3) && (c == 3 || (5 / b > 1 && condition)))
        {
            return a;
        }
        else if (condition == false)
        {
            return b;
        }
        else
        {
            return c;
        }
    }

    public static int QuadraticFirstRoot(int a, int b, int c)
    {
        int D = b * b - 4 * a * c;
        int dividend = 0;

        if (D > 0)
        {
            dividend = -b;
            dividend += (int)Math.Sqrt(D);
        }
        else if (D == 0)
        {
            dividend = -b;
        }
        else
        {
            Debug.Assert(D < 0);
            Trace.WriteLine(-1);
            throw new ArgumentException();
        }

        int divisor = (2 * a);
        Debug.Assert(divisor != 0);
        return dividend / divisor;
    }

    public static int ComplexExample(int a, int b, bool c)
    {
        if (a == 0)
        {
            return -b;
        }

        if (IsNiceNumber(a) && a / b > 2 && b != -1)
        {
            int d = b;
            while (a < b)
            {
                a += a;
                Debug.Assert(a != 0);
            }
        }
        else
        {
            a += b;
        }

        return a;
    }

    public static bool IsNiceNumber(int i)
    {
        if (i % 2 == 0)
        {
            return true;
        }
        else if (i % 3 == 0)
        {
            return true;
        }
        else
        {
            return false;
        }
    }
}
