// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;
using System.Runtime.CompilerServices;
using Arc.Collections;

namespace Netsphere.Net;

public class CubicCongestionControl : ICongestionControl
{
    private const int CubicThreshold = 10;
    private const double InitialCwnd = 10;
    private const double MaxCwnd = 1_000_000;
    private const double MinCwnd = 2;
    private const double BurstRatio = 1.5d;
    private const double BurstRatioInv = 1.0d / BurstRatio;
    private const double Beta = 0.2d;
    private const double WtcpRatio = 3d * Beta / (2d - Beta);
    private const double FastConvergenceRatio = (2d - Beta) / 2d;
    private const ulong CubicFactor = (1ul << 40) / 410ul;
    private const double BrakeThreshold = 0.05d;

    private static ReadOnlySpan<byte> v =>
    [
        0, 54, 54, 54,  118, 118, 118, 118,
        123,  129,  134,  138,  143,  147,  151,  156,
        157,  161,  164,  168,  170,  173,  176,  179,
        181,  185,  187,  190,  192,  194,  197,  199,
        200,  202,  204,  206,  209,  211,  213,  215,
        217,  219,  221,  222,  224,  225,  227,  229,
        231,  232,  234,  236,  237,  239,  240,  242,
        244,  245,  246,  248,  250,  251,  252,  254,
    ];

    public static uint CubicRoot(ulong a)
    {
        var b = 64 - BitOperations.LeadingZeroCount(a);
        if (b < 7)
        {
            return ((uint)v[(int)a] + 35) >> 6;
        }

        b = ((b * 84) >> 8) - 1;
        var shift = (int)(a >> (b * 3));
        var x = (((uint)v[shift] + 10) << b) >> 6;
        x = (2 * x) + (uint)(a / ((ulong)x * (ulong)(x - 1)));
        x = (x * 341) >> 10;
        return x;
    }

    public CubicCongestionControl(Connection connection)
    {
        this.Connection = connection;
        this.cwnd = InitialCwnd;
        this.ssthresh = MaxCwnd;
        this.slowstart = true;
        this.UpdateRegen();

        if (connection.IsClient)
        {
            this.logger = this.Connection.ConnectionTerminal.NetBase.UnitLogger.GetLogger<CubicCongestionControl>();
        }
    }

    #region FieldAndProperty

    public Connection Connection { get; set; }

    public int NumberInFlight
        => this.numberInFlight;

    public bool IsCongested
        => this.numberInFlight >= this.capacityInt;

    public double FailureRatio
    {
        get
        {
            var total = this.positiveFactor + this.negativeFactor;
            if (total == 0)
            {
                return 0;
            }
            else
            {
                return this.negativeFactor / total;
            }
        }
    }

    private readonly ILogger? logger;

    private readonly object syncObject = new();
    private int numberInFlight;

    // Smoothing transmissions
    private double capacity; // Equivalent to cwnd, but increases gradually to prevent mass transmission at once.
    private int capacityInt; // (int)this.capacity
    private double regen; // Number of packets that can be transmitted per ms.
    private double boost; // Number of packets that can be boost-transmitted per ms.
    private long boostMicsMax; // Upper limit of boost time.
    private long boostMics; // Remaining boost time.

    // Cubic
    private bool capacityLimited;
    private int cubicCount;
    private int ackCount;
    private double increasePerAck; // 1 / this.cnt
    private double cwnd; // Upper limit of the total number of genes in-flight.
    private double tcpCwnd;
    private double ssthresh;
    private ulong k;
    private double originPoint;
    private bool slowstart;

    // Packet loss
    private long epochStart;
    private double lastMaxCwnd;
    private long validDeliveryMics; // Mics greater than this value are treated as valid.
    private uint deliverySuccess;
    private uint deliveryFailure;
    private double positiveFactor;
    private double negativeFactor;
    private double power;

    // Test
    // private int testCount;

    #endregion

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void ICongestionControl.AddInFlight(SendGene sendGene, long rto)
        => Interlocked.Increment(ref this.numberInFlight);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void ICongestionControl.RemoveInFlight(SendGene sendGene, bool ack)
    {
        Interlocked.Decrement(ref this.numberInFlight);
        if (ack)
        {
            Interlocked.Increment(ref this.ackCount);
            this.ReportDeliverySuccess();
        }
    }

    public void ReportDeliverySuccess()
        => Interlocked.Increment(ref this.deliverySuccess);

    public void ReportDeliveryFailure()
        => Interlocked.Increment(ref this.deliveryFailure);

    bool ICongestionControl.Process(NetSender netSender, long elapsedMics, double elapsedMilliseconds)
    {// lock (ConnectionTerminal.CongestionControlList)
        if (this.Connection.IsDisposed)
        {// The connection is disposed.
            return false;
        }

        lock (this.syncObject)
        {// To prevent deadlocks, the lock order for CongestionControl must be the lowest, and it must not acquire locks by calling functions of other classes.
            // CongestionControl: Positive/Negative factor
            this.ProcessFactor();

            // CongestionControl: Cubic (cwnd)
            this.capacityLimited |= (this.numberInFlight * 8) >= (this.capacityInt * 7);
            if (this.cubicCount++ >= CubicThreshold)
            {
                if (this.capacityLimited)
                {// Capacity limited
                    this.capacityLimited = false;
                    this.UpdateCubic((double)this.ackCount);
                    this.UpdateRegen();

                    Console.WriteLine($"cwnd:{this.cwnd:F2} {this.increasePerAck:F3} epoch:{this.epochStart} k:{this.k:F2} tcp:{this.tcpCwnd:F2}");
                }

                this.cubicCount = 0;
                this.ackCount = 0;
            }

            // CongestionControl: Capacity
            var capacityLimited = this.CalculateCapacity(elapsedMics, elapsedMilliseconds);
            this.capacityInt = (int)this.capacity;

            // this.logger?.TryGet(LogLevel.Debug)?.Log($"{(capacityLimited ? "CAP " : string.Empty)}current/cap/cwnd {this.NumberOfGenesInFlight}/{this.capacity:F1}/{this.cwnd:F1} regen {this.regen:F2} boost {this.boost:F2} mics/max {this.boostMics}/{this.boostMicsMax}");
        }

        return true;
    }

    private void ProcessFactor()
    {
        if (this.cubicCount == 0 || this.power == 0)
        {
            this.power = Math.Pow(0.5, 1000d / this.Connection.MinimumRtt);
        }

        if (Mics.FastSystem < this.validDeliveryMics)
        {
            Volatile.Write(ref this.deliverySuccess, 0);
            Volatile.Write(ref this.deliveryFailure, 0);
            return;
        }

        this.positiveFactor += Volatile.Read(ref this.deliverySuccess);
        this.negativeFactor += Volatile.Read(ref this.deliveryFailure);
        Volatile.Write(ref this.deliverySuccess, 0);
        Volatile.Write(ref this.deliveryFailure, 0);

        var failureFactor = this.FailureRatio;
        if (failureFactor > BrakeThreshold)
        {
            Console.WriteLine($"Brake: {failureFactor:F3}");
            Console.WriteLine($"+{this.positiveFactor:F3} -{this.negativeFactor:F3} : {failureFactor:F3} power {this.power:F3}");

            this.positiveFactor = 0;
            this.negativeFactor = 0;
            this.Brake();
        }

        // Console.WriteLine($"+{this.positiveFactor:F3} -{this.negativeFactor:F3} : {failureFactor:F3} power {this.power:F3}");

        this.positiveFactor *= this.power;
        this.negativeFactor *= this.power;
    }

    private void Brake()
    {
        this.validDeliveryMics = Mics.FastSystem + this.Connection.MinimumRtt;

        this.epochStart = 0;
        if (this.cwnd < this.lastMaxCwnd)
        {// Fast convergence
            this.lastMaxCwnd = this.cwnd * FastConvergenceRatio;
        }
        else
        {
            this.lastMaxCwnd = this.cwnd;
        }

        this.cwnd *= 1 - Beta;
        if (this.cwnd < MinCwnd)
        {
            this.cwnd = MinCwnd;
        }

        this.ssthresh = this.cwnd;
    }

    private bool CalculateCapacity(long elapsedMics, double elapsedMilliseconds)
    {
        if (this.capacityInt <= this.numberInFlight)
        {// Capacity limited
            if (this.boostMics > 0)
            {// Boost
                this.capacity += this.boost * elapsedMilliseconds;

                if (this.capacity >= this.cwnd)
                {
                    this.capacity = this.cwnd;

                    this.boostMics += elapsedMics;
                    if (this.boostMics > this.boostMicsMax)
                    {
                        this.boostMics = this.boostMicsMax;
                    }

                    return true;
                }
                else
                {
                    this.boostMics -= elapsedMics;
                    return true;
                }
            }
            else
            {// Normal
                this.capacity += this.regen * elapsedMilliseconds;

                if (this.capacity >= this.cwnd)
                {
                    this.capacity = this.cwnd;

                    this.boostMics += elapsedMics;
                    if (this.boostMics > this.boostMicsMax)
                    {
                        this.boostMics = this.boostMicsMax;
                    }

                    return true;
                }
                else
                {
                    return true;
                }
            }
        }
        else
        {// Not capacity limited (capacity > this.genesInFlight.Count).
            var upperLimit = this.numberInFlight + this.boost;
            if (this.capacity > upperLimit)
            {
                this.capacity = upperLimit;
            }

            this.boostMics += elapsedMics;
            if (this.boostMics > this.boostMicsMax)
            {
                this.boostMics = this.boostMicsMax;
            }

            return false;
        }
    }

    private void UpdateCubic(double acked)
    {
        if (this.slowstart)
        {// Slow start
            var cwnd = Math.Min(this.cwnd + acked, this.ssthresh);
            acked -= cwnd - this.cwnd;
            this.cwnd = cwnd; // MaxCwnd

            if (acked == 0)
            {
                return;
            }
        }

        if (this.epochStart == 0)
        {
            this.epochStart = Mics.FastSystem;
            if (this.cwnd < this.lastMaxCwnd)
            {
                this.k = CubicRoot(CubicFactor * (ulong)(this.lastMaxCwnd - this.cwnd));
                this.originPoint = this.lastMaxCwnd;
            }
            else
            {
                this.k = 0;
                this.originPoint = this.cwnd;
            }

            this.tcpCwnd = this.cwnd;
        }

        var t = (ulong)(Mics.FastSystem + this.Connection.MinimumRtt - this.epochStart); // Mics
        t >>= 10; // Milliseconds

        ulong offset;
        double target;
        if (t > this.k)
        {
            offset = t - this.k;
            target = this.originPoint + ((410ul * offset * offset * offset) >> 40);
        }
        else
        {
            offset = this.k - t;
            target = this.originPoint - ((410ul * offset * offset * offset) >> 40);
        }

        if (target > this.cwnd)
        {
            this.increasePerAck = (target - this.cwnd) / this.cwnd;
        }
        else
        {
            this.increasePerAck = 0.01d / this.cwnd;
        }

        if (this.lastMaxCwnd == 0 && this.increasePerAck < 0.05d)
        {
            this.increasePerAck = 0.05d;
        }

        // Tcp friendliness
        this.tcpCwnd += WtcpRatio * acked / this.cwnd;
        if (this.tcpCwnd > this.cwnd)
        {
            var ratio = (this.tcpCwnd - this.cwnd) / this.cwnd;
            if (this.increasePerAck < ratio)
            {
                this.increasePerAck = ratio;
            }
        }

        this.increasePerAck = Math.Min(this.increasePerAck, 0.5);
        this.cwnd += acked * this.increasePerAck; // MaxCwnd
    }

    private void UpdateRegen()
    {
        var r = this.cwnd / this.Connection.SmoothedRtt * 1000;
        this.regen = r;
        this.boost = r * BurstRatio;
        this.boostMicsMax = (long)(this.Connection.SmoothedRtt * BurstRatioInv); // RTT / BurstRatio
    }
}
