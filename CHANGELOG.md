# Kerbal Simpit Changelog

## v1.2.9 (2018-10-21)

Built against KSP 1.5.1.

## v1.2.8 (2018-06-30)

Built against KSP 1.4.4.

## v1.2.7 (2018-05-04)

Built against KSP 1.4.3.

## v1.2.6 (2018-03-29)

Built against KSP 1.4.2.

## v1.2.5 (2018-03-16)

* Partial fix for broken throttle handler. The receive handler is
  set up properly now, but it looks like it's still getting buggy
  values.
* Added debug logging around the throttle handler. With verbose
  set to true in the config file, all decoded throttle packets
  are logged.

## v1.2.4 (2018-03-14)

Built against KSP 1.4.1.

## v1.2.3 (2018-03-11)

Built against KSP 1.4.

## v1.2.2 (2017-09-06)

Built against KSP 1.3.1 prerelease.

## v1.2.1 (2017-07-03)

* Serial port wrapper now uses an internal queue, to prevent multiple
  threads trying to write to the port. Should prevent TimeoutExceptions.
* Increased default RefreshRate to 125.

## v1.2.0 (2017-06-24)

* Added new channels for flight control state handling:
  * Rotation of active vessel
  * Translation of active vessel
  * Wheel steer and throttle of active vessel
  * Main throttle of active vessel

## v1.1.1 (2017-06-20)

* Fix action group status sending.

## v1.1 (2017-06-01)

* Fix nullrefs from the resource provider when Alternate Resource Panel
  is not installed.
* Add channel for SoI of active vessel.

## v1.0 (2017-05-28)

First stable release. Changed names across both this and the Arduino
projects to be consistent.

## v0.10 prerelease (2017-05-26)

Built against KSP 1.3. Update metadata to only support 1.3.

## v0.9 prerelease (2017-05-22)

Hopefully the last prerelease.

* Hardcoded `Documentation` parameter in config file. The config class
  keeps this paramater as static, pointing to a URL giving info on the
  config file format.
* RefreshRate parameter now exposed in the configuration. This is a
  global parameter - every provider that wants to send data
  periodically will send to all devices that want that provider's
  data once per `RefreshRate` milliseconds.
* New providers:
  * `ApsidesTime` gives time to next apoapsis and periapsis, in seconds.
  * `TargetInfo` gives information about any object the active vessel has
    targetted. Both distance to target and relative velocity are sent.

## v0.8 prerelease (2017-05-18)

* Added channels for all stock resources. This is done by depending on the
Alternate Resource Panel mod.
* Added apsides channel (apoapsis and periapsis).
* Added velocity channel (surface, orbital and vertical).
* Added a channel that reports on action group status. The format is identical
to the command channel for AGs.
* Changed the underlying serial driver again. The library now includes its
own driver, a straight fork of the System.IO.Ports class from mono. There's
been a lot of internal restructuring to let this happen.
* Build and deploy system has been overhauled.

## v0.7 prerelease (2017-05-01)

Big focus on getting the KSPSerialPort class receiving properly on Windows.

* Switched from SerialPortLib2 to PsimaxSerial for the underlying serial
driver. This will soon be migrated (again), to my own fork of mono's
System.IO.Ports class.
* Added a new serial read thread to, that just periodically polls.
* Currently using regex matching on the serial port name and initialisation:
  * COM ports imply Windows, and the new serial polling read thread is used.
  * For all other ports, the existing async event handler thread is used.

## v0.6 prerelease (2017-04-24)

* Add support for standard action groups (staging, abort, lights etc). As
with custom AGs, packets for enable, disable and toggle have been added.

## v0.5 prerelease (2017-04-22)

* Add toggle Custom AG packet and handler.

## v0.4 prerelease (2017-04-20)

* Fix off-by-one errors in the array iteration loops. This was most
obvious in the staging handler, but potentially happening elsewhere.

## v0.3 prerelease (2017-04-01)

* Modify Staging handler to accept byte array payload (can now enable and
disable the staging AG in a single packet).

## v0.2 prerelease (2017-03-29)

* Custom Action Group support added (including Action Groups Extended
integration)

## v0.1 prerelease (2017-03-26)

* Asynchronous serial read thread complete.
* Echo handler implemented.
* Staging test handler implemented.
* Altitude handler implemented.
* Initial release.
