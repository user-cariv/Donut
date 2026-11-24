package main

import (
	"fmt"
	"net"
	"os"
	"strconv"
	"strings"
	"time"

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
	if err := run(); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}

func run() error {
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

	mutex, err := windows.CreateMutex(nil, false, windows.StringToUTF16Ptr("Global\\DonutBoostSystemMerge"))
	if mutex != 0 {
		defer windows.CloseHandle(mutex)
	}
	if err != nil {
		return err
	}

	if _, err := net.InterfaceByName("net-merge"); err == nil {
		return fmt.Errorf("another system merge tunnel is active")
	}

	adapters, err := nics.DiscoverNics(nil, nil)
	if err != nil {
		return err
	}
	if len(adapters) == 0 {
		return fmt.Errorf("no usable network adapters")
	}
	scheduler := engine.NewScheduler(adapters)
	scheduler.SetAuto(true)
	balancer := engine.NewBalancer(scheduler)
	balancer.SetV6Mode(engine.V6Auto)
	tunnel := tun.New(balancer)
	if err := tunnel.On(); err != nil {
		return err
	}
	defer tunnel.Close()

	done := make(chan struct{})
	defer close(done)
	go scheduler.RunProbes(done)
	if err := windows.SetEvent(ready); err != nil {
		return err
	}
	last := time.Now()
	nextScan := last.Add(3 * time.Second)
	for {
		result, err := windows.WaitForMultipleObjects([]windows.Handle{stop, parent}, false, 1000)
		if err != nil {
			return err
		}
		if result != uint32(windows.WAIT_TIMEOUT) {
			return nil
		}
		now := time.Now()
		if !now.Before(nextScan) {
			if adapters, err := nics.DiscoverNics(nil, nil); err == nil {
				scheduler.Update(adapters)
			}
			nextScan = now.Add(3 * time.Second)
		}
		scheduler.Tick(now.Sub(last))
		last = now
	}
}
