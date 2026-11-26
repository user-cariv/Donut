package engine

import "math"

// Keep flow count as a reservation for new sockets whose traffic has not been sampled.
func donutFlowScore(slot *Slot, family Family, busy int64, automatic bool) float64 {
	score := float64(busy+1) / slot.weight[family]
	peak := slot.peak[family]
	if !automatic || peak < minMeasuredPeak {
		return score
	}

	relayRate := slot.rateIn[family] + slot.rateOut[family]
	linkRate := slot.linkRateIn + slot.linkRateOut
	capacity := peak
	if limit := slot.limit.Load(); limit != nil {
		// The upstream cap is per direction. A recently capped link can have an
		// old, much higher peak, so weight new work by its current ceiling.
		capacity = math.Min(peak, limit.mbitPerSec*bytesPerMbit)
		score *= peak / capacity
		relayRate = math.Max(slot.rateIn[family], slot.rateOut[family])
		linkRate = math.Max(slot.linkRateIn, slot.linkRateOut)
	}
	utilization := math.Min(1, math.Max(relayRate, linkRate)/capacity)
	// Recent utilization breaks close calls. Keep measured capacity dominant
	// so a busy broadband link does not send a burst to a much slower tether.
	return score * (1 + 0.35*utilization*utilization)
}
