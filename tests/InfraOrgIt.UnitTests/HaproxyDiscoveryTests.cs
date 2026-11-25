using InfraOrgIt.Api.Features.Haproxy;

namespace InfraOrgIt.UnitTests;

public sealed class HaproxyDiscoveryTests
{
    [Fact]
    public void Parses_a_running_haproxy_with_dataplane()
    {
        const string output = """
            __INFRAORGIT_HAPROXY__
            bin=/usr/sbin/haproxy
            version=HAProxy version 2.8.30-0ubuntu0.24.04.2 2026/10/27
            active=active
            enabled=enabled
            config=/etc/haproxy/haproxy.cfg
            dataplane=1
            __PORTS__
            22
            443
            5555
            5556
            """;

        var info = HaproxyDiscovery.Parse(output);

        Assert.True(info.HasHaproxy);
        Assert.True(info.Active);
        Assert.True(info.EnabledAtBoot);
        Assert.Equal("/usr/sbin/haproxy", info.Binary);
        Assert.Contains("2.8.30", info.Version);
        Assert.Equal("/etc/haproxy/haproxy.cfg", info.ConfigPath);
        Assert.Equal(1, info.DataPlaneProcesses);
        Assert.Equal(5555, info.ApiPort);
        Assert.Contains(5556, info.ListeningPorts);
    }

    [Fact]
    public void Parses_a_stopped_haproxy_without_dataplane()
    {
        const string output = """
            __INFRAORGIT_HAPROXY__
            bin=/usr/sbin/haproxy
            version=HAProxy version 2.4.30
            active=inactive
            enabled=disabled
            config=/etc/haproxy/haproxy.cfg
            dataplane=0
            __PORTS__
            22
            """;

        var info = HaproxyDiscovery.Parse(output);

        Assert.True(info.HasHaproxy);
        Assert.False(info.Active);
        Assert.False(info.EnabledAtBoot);
        Assert.Null(info.ApiPort);
    }

    [Fact]
    public void Parses_a_server_without_haproxy()
    {
        const string output = """
            __INFRAORGIT_HAPROXY__
            bin=
            active=inactive
            enabled=
            config=
            dataplane=0
            __PORTS__
            22
            """;

        var info = HaproxyDiscovery.Parse(output);

        Assert.False(info.HasHaproxy);
        Assert.Null(info.Version);
        Assert.Null(info.ApiPort);
    }

    [Fact]
    public void Falls_back_to_5555_when_dataplane_has_no_known_port()
    {
        const string output = """
            __INFRAORGIT_HAPROXY__
            bin=/usr/sbin/haproxy
            version=3.2.25
            active=active
            config=/etc/haproxy/haproxy.cfg
            dataplane=2
            __PORTS__
            22
            9000
            """;

        var info = HaproxyDiscovery.Parse(output);

        Assert.Equal(5555, info.ApiPort);
    }
}
