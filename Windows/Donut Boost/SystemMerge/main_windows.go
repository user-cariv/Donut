package main

import (
	"fmt"
	"net"
	"net/netip"
	"os"
	"strconv"
	"strings"
	"sync/atomic"
	"time"
	"unsafe"

	"golang.org/x/sys/windows"
	"net-merge/internal/engine"
	"net-merge/internal/nics"
	"net-merge/internal/tun"
)

func main() {
	if len(os.Args) == 2 && os.Args[1] == "--check" {
		fmt.Println("Donut Boost System Merge")
		return
	}
	if err := runSystemMerge(); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}

func runSystemMerge() (runErr error) {
	if len(os.Args) != 3 || !windows.GetCurrentProcessToken().IsElevated() {
		return fmt.Errorf("start System merge from Donut Boost")
	}
	pid, err := strconv.ParseUint(os.Args[1], 10, 32)
	if err != nil || pid == 0 {
		return fmt.Errorf("invalid parent process")
	}
	session := os.Args[2]
	if len(session) != 32 || strings.Trim(session, "0123456789abcdef") != "" {
		return fmt.Errorf("invalid session")
	}
	parent, err := windows.OpenProcess(windows.SYNCHRONIZE, false, uint32(pid))
	if err != nil {
		return err
	}
	defer windows.CloseHandle(parent)
	openEvent := func(suffix string, access uint32) (windows.Handle, error) {
		name := windows.StringToUTF16Ptr("Local\\DonutBoostMerge-" + session + suffix)
		return windows.OpenEvent(access, false, name)
	}
	stop, err := openEvent("-Stop", windows.SYNCHRONIZE)
	if err != nil {
		return err
	}
	defer windows.CloseHandle(stop)
	ready, err := openEvent("-Ready", windows.EVENT_MODIFY_STATE)
	if err != nil {
		return err
	}
	defer windows.CloseHandle(ready)

	report, closeReport, err := openReport(session)
	if err != nil {
		return err
	}
	defer closeReport()
	defer func() {
		if runErr != nil {
			report(runErr.Error())
		}
	}()

	mutex, err := windows.CreateMutex(nil, false, windows.StringToUTF16Ptr("Global\\DonutBoostSystemMerge"))
	if mutex != 0 {
		defer windows.CloseHandle(mutex)
	}
	if err != nil {
		return err
	}

	if upstreamTunnelActive() {
		return fmt.Errorf("Net Merge is active. Turn it off, then reopen Donut Boost.")
	}

	handles := []windows.Handle{stop, parent}
	report("Waiting for a network adapter.")
	var adapters []nics.Nic
	var skipped []nics.Skip
	for len(adapters) == 0 {
		adapters, skipped, err = nics.DiscoverNicsDetailed(nil, nil)
		if err != nil {
			return err
		}
		if len(adapters) != 0 {
			break
		}
		report(summarizeLinks(nil, skipped))
		result, err := windows.WaitForMultipleObjects(handles, false, 1000)
		if err != nil || result != uint32(windows.WAIT_TIMEOUT) {
			return err
		}
	}
	scheduler := engine.NewScheduler(adapters)
	scheduler.SetAuto(true)
	balancer := engine.NewBalancer(scheduler)
	balancer.SetV6Mode(engine.V6Auto)
	done := make(chan struct{})
	defer close(done)
	go scheduler.RunProbes(done)
	updates := make(chan adapterScan, 1)
	go scanAdapters(done, updates)
	tunnel := tun.New(balancer)
	defer tunnel.Close()

	report("Using normal Windows routing while merge waits for two working links. " + summarizeLinks(scheduler.Snapshot(), skipped))
	if err := windows.SetEvent(ready); err != nil {
		return err
	}
	last := time.Now()
	pausePolicy := autoPauser{enabled: true}
	pausePolicy.start(true)
	var restoreError error
	for {
		result, err := windows.WaitForMultipleObjects(handles, false, 1000)
		if err != nil {
			return err
		}
		if result != uint32(windows.WAIT_TIMEOUT) {
			return nil
		}
		now := time.Now()
		select {
		case scan := <-updates:
			scheduler.Update(scan.adapters)
			skipped = scan.skipped
		default:
		}
		scheduler.Tick(now.Sub(last))
		slots := scheduler.Snapshot()
		switch pausePolicy.tick(now, tunnel.IsOn(), healthyLinkCount(slots), movingFlows(slots)) {
		case autoPause:
			tunnel.Off()
		case autoResume:
			if err := tunnel.On(); err != nil {
				tunnel.Off()
				pausePolicy.resumeFailed()
				restoreError = err
			} else {
				restoreError = nil
			}
		}
		message := summarizeLinks(slots, skipped)
		if !tunnel.IsOn() {
			message = "Using normal Windows routing while merge waits for two working links. " + message
			if restoreError != nil {
				message += fmt.Sprintf(" Last merge attempt: %v", restoreError)
			}
		}
		report(message)
		last = now
	}
}

type adapterScan struct {
	adapters []nics.Nic
	skipped  []nics.Skip
}

func scanAdapters(done <-chan struct{}, updates chan<- adapterScan) {
	ticker := time.NewTicker(2 * time.Second)
	defer ticker.Stop()
	for {
		select {
		case <-done:
			return
		case <-ticker.C:
			adapters, skipped, err := nics.DiscoverNicsDetailed(nil, nil)
			if err != nil {
				continue
			}
			select {
			case updates <- adapterScan{adapters, skipped}:
			case <-done:
				return
			}
		}
	}
}

func healthyLinkCount(slots []engine.SlotStats) int {
	count := 0
	for _, slot := range slots {
		if slot.Present && (slot.State == "up" || slot.State6 == "up") {
			count++
		}
	}
	return count
}

func movingFlows(slots []engine.SlotStats) int64 {
	var count int64
	var rate float64
	for _, slot := range slots {
		if slot.Present {
			count += slot.Active
			rate += slot.RateIn + slot.RateOut
		}
	}
	if rate < quietRate {
		return 0
	}
	return count
}

func upstreamTunnelActive() bool {
	iface, err := net.InterfaceByName("net-merge")
	if err != nil || iface.Flags&net.FlagUp == 0 {
		return false
	}
	addresses, err := iface.Addrs()
	if err != nil {
		return true
	}
	captureRange := netip.MustParsePrefix("198.18.0.0/15")
	for _, address := range addresses {
		prefix, err := netip.ParsePrefix(address.String())
		if err == nil && captureRange.Contains(prefix.Addr()) {
			return true
		}
	}
	return false
}

func summarizeLinks(slots []engine.SlotStats, skipped []nics.Skip) string {
	var lines []string
	for _, slot := range slots {
		if !slot.Present {
			continue
		}
		traffic := ""
		if slot.BytesIn > 0 {
			traffic = ", data received"
		}
		lines = append(lines, fmt.Sprintf("%s (IPv4 %s, IPv6 %s%s)", slot.Name, slot.State, slot.State6, traffic))
	}
	if len(lines) == 0 {
		lines = append(lines, "No eligible network links")
	}
	for _, adapter := range skipped {
		if strings.HasPrefix(adapter.Reason, "no default gateway") || adapter.Reason == "no usable IP address" {
			lines = append(lines, fmt.Sprintf("%s excluded (%s)", adapter.Name, adapter.Reason))
		}
	}
	return "Links: " + strings.Join(lines, "; ")
}

func openReport(session string) (func(string), func(), error) {
	const size = 4096
	name := windows.StringToUTF16Ptr("Local\\DonutBoostMerge-" + session + "-Report")
	kernel := windows.NewLazySystemDLL("kernel32.dll")
	handle, _, callErr := kernel.NewProc("OpenFileMappingW").Call(windows.FILE_MAP_WRITE, 0, uintptr(unsafe.Pointer(name)))
	if handle == 0 {
		return nil, nil, callErr
	}
	defer windows.CloseHandle(windows.Handle(handle))
	address, err := windows.MapViewOfFile(windows.Handle(handle), windows.FILE_MAP_WRITE, 0, 0, size)
	if err != nil {
		return nil, nil, err
	}
	buffer := unsafe.Slice((*byte)(unsafe.Pointer(address)), size)
	sequence := (*uint32)(unsafe.Pointer(address))
	length := (*uint32)(unsafe.Pointer(address + 4))
	var previous string
	write := func(message string) {
		if message == previous {
			return
		}
		previous = message
		atomic.AddUint32(sequence, 1)
		n := copy(buffer[8:], message)
		atomic.StoreUint32(length, uint32(n))
		atomic.AddUint32(sequence, 1)
	}
	return write, func() { _ = windows.UnmapViewOfFile(address) }, nil
}
