线路测试程序说明书
===
### 简介
设计这个线路测试程序，起因是为了测试一段网络线路的质量。由于工作原因，设计成可以用于测试加速器游戏线路的质量，也能用来测验普通家庭宽带的网络质量。

这个线路测试程序，基于TCP/UDP开发，利用数据回显的方式，用来衡量各种网络负载情况下的表现。也就是说，要使用这个测试程序，首先你要准备一个回显服务器（可以出门左转EchoServerCore那个repo）。

测试程序可以统计测试过程中的**发包速度、网络延迟、丢包率 和 发送带宽**。

运行这段程序比较简单，在安装了.net core runtime的机器上，进入到代码目录下，执行下列命令即可：

```
dotnet run ConfigDirectGuangzhou.xml
```

![测试家里网络到广州接入点的连接质量](https://i.loli.net/2018/10/24/5bd0341bf1cd7.png)

### 原理

#### TCP和UDP部分

我们知道，TCP在listenSocket上accept时，会创建新的socket与客户端连接。新socket可以派发给不同线程处理，通过五元组（source ip/port tcp target ip/port）来区分不同的连接。而UDP只能在一个listenSocket上反复投递recvfrom和sendto请求（在不开端口重用和地址重用的前提下）。于是UDP压测的client和server代码写出来几乎是一模一样的：

1. 多个发送线程，每个线程有一条发送队列。UDP client的公共send函数，只是往队列里push一条数据，其中包体数据从内存池中申请，避免反复发包时不断开辟内存。这样send就会变得很快，调用完立刻返回，而这些发送线程也能旋即从队列中取出包来，发出去。
2. 在listenSocket上使用较大的缓冲区，投递异步的recvfrom请求，响应回包时使用上面的快速send，最大限度降低从接收缓冲区取数据并做出响应的时间。

TCP和UDP的client部分，采用抽象出INetClient接口、底层使用[socket.core](https://github.com/fengma312/socket.core)的实现。

```csharp
/// <summary>
/// 协议无关的网络客户端接口
/// </summary>
interface INetClient
{
    bool Connect(string ip, int port);
    bool Send(byte[] data, int offset, int length);
    event Action<byte[], int, int> OnReceive;
    event Action<int> OnSend;
}
```

#### 压测逻辑部分

在执行压测时，构造了三种模式：

1. 固定间隔发包（**interval**模式）
2. 固定速度（pps）发包（**onsend**模式）
3. 先热身，再稳定（**onrecv**模式）

第三种是周末琢磨出来的新方法，用具体数字说明，我先往发送队列慢慢投递10W个发送请求，然后不再投递发送请求。而是在收到服务端回包之后，再投递下一个发送请求。在经过热身阶段之后，会进入稳定阶段。稳定阶段的发包速度，也就是这个UDP连接的最大不丢包pps。如果网络状况很差，这个值会随着发包进行，逐渐衰减到0，这样的网络基本可以去投诉了。

调整完的压测程序，可以用第二种模式，来绘制pps和丢包率的曲线，用第三种模式，来获得比较精确的最大不丢包pps。下面部分和详细介绍发包模式。

#### 延迟统计部分

另外，为了追溯每个网络包的延迟，肯定不能单线程收发包。为了追溯同时出去的n个包的延迟，必须给每个包做染色处理。既然整个程序基于回显服务，那么让每个包携带的回显数据不同即可。

每个包携带的数据是一个32位整形数，也就限制了本程序最大能测的并发量级是2^32pps。发包时标记发送时间，收到包时用当前时间减去标记时间，就能得到这个包经历的延迟。

发包控制、数据统计，都是通过异步发送/接收的回调函数实现的。

在代码实现上，每个类负责如下功能：

- NetClient.cs：负责封装socket.core，向上以`INetClient`接口的形式提供网络功能
- ProtocolWrapper.cs：负责封解包，和添加自定的网络协议
- PearTestRunner.cs：负责控制“鸭梨”测试的发包流程
- MetricManager.cs：负责通过回调函数统计延迟、速度、丢包信息
- Logger.cs：负责打不同级别的log

### 测试目标

测试目标在`target`节点进行配置，`ip`可以填IPv4或者IPv6地址，或者填写域名也可以。

在`general`节点的`protocol`属性处指定使用UDP还是TCP协议，右侧的`client count`表示客户端连接数，一般指定为1即可，如果需要模拟多个客户端同时测试，编辑成50以下的数值比较合适。

### 发包模式

发包模式有三种，可以在xml文件的`control`节点的`mode`属性上配置，可配置的三种模式分别是：

- interval：以固定时间间隔发包，时间单位为毫秒，配置在右边的`interval`属性处。请注意，间隔为n毫秒时，发包速度不一定等于1000/n，如果需要固定速度发包，请使用下面的onsend模式
- onsend：以固定发包速度发包，单位是pps，可以在右边的`maxpps`属性处配置最大发包速度，`ppsdelay`设置成10以内的数字即可。在测试一般情况而不是压测时，建议将`maxpps`配置成不到2000的数值
- onrecv：收到回显数据就继续发包，也就是发一个包，在收到回包的时候、再发送下一个包，这种模式可以测量出**当前客户端理论最大不丢包发包速度**。可以在右边的`warmupcount`处配置热身包的数量，在`warmupinterval`处配置热身包的间隔。测试程序在发送完热身包之后，就会完全进入到类似“接力发送”的状态。请注意，在高丢包环境下，onrecv模式会越来越慢、直到停止，例如10%的丢包率，在反复收发20次之后，发包速度就会只有开始时的1/10，30次之后就基本停滞了

### 流程控制

测试程序什么时候停止呢？可以让测试程序在发完一定数量的网络包之后停止、或者测试一定时间后停止、或者持续运行直到手动停止。

在执行测试时，如果需要手动中止测试程序，按任意键即可。

控制什么时候停止发包的配置，对应`control`节点的`mode`属性，可配置的三种模式分别是：

- count：发送一定数量的网络包，param就表示包数
- time：测试一定时间，param就表示毫秒数
- manual：持续发包，直到手动中止测试程序

### 数据协议

测试程序默认使用普通回显服务进行测试，但是为了支持走加速器的网络加速通道，添加了数据协议元素，即`dataprotocol`元素。

```csharp
/// <summary>
/// 协议封装接口
/// </summary>
public interface IProtocolWrapper
{
    bool PackProtocol(ref byte[] data, ref int offset, ref int length);
    bool UnpackProtocol(ref byte[] data, ref int offset, ref int length);
}
```

- 把`dataprotocol`元素的`mode`属性配置成 raw，就是普通的回显测试，纯回显
- 如果配置成 fakes5，就是加速器的经典LSP协议，也叫(伪)S5协议，开源版本去掉了该协议的支持
- 开发者可以根据自己项目的需求，继承IProtocolWrapper接口，实现自己的协议

### 其他

测试程序停止后，会打印一份完整数据。在测试过程中，也能定期打印阶段性的数据。

可以发送n个包就打一次日志、或者间隔n毫秒就打一次日志，可以在`log`节点的`mode`属性处进行配置：

- count：发送n个包就打一次日志
- time：间隔n毫秒就打一次日志

测试程序会提前准备好所有要发送的数据，放到“发送池”里。这是用空间换时间的设计，可以`debug`节点的`sendpoolsize`属性处指定发送池的大小。发包的时候程序会从发送池取数据，收到回包的时候会将数据还给发送池。一般建议保持100万不变即可，如果发送池过小、而发包速度又很快的话，会导致发送池枯竭；如果发送池过大，会导致测试程序初始化变得很慢。

### 示例

如下是`ConfigTemplate.xml`包含的内容：

```xml
<?xml version="1.0" encoding="utf-8"?>
<settings>
    <!--常规配置-->
    <!--protocol: udp(复用socket发送udp包), tcp(以长连接的方式进行测试)-->
    <!--clientcount: client的数量-->
    <!--threadsperclient: 每个client的发送线程数-->
    <!--buffersize: 接收缓冲区大小-->
    <general protocol="udp" clientcount="1" threadsperclient="8" buffersize="2048" />
    <!--目标-->
    <!--自测时可以直接在本地搭一个UDP回显-->
    <!--ip: IPv4或者IPv6地址-->
    <!--port: 端口-->
    <target ip="127.0.0.1" port="7777" />
    <!--发包控制-->
    <!--你是要发一定数量,还是发一段时间,还是手动停止呢-->
    <!--mode: count(发一定数量即可), time(发一段时间即可), manual(手动停止发包)-->
    <!--param: count模式下表示发包数量, time模式下表示发包时间(毫秒), manual模式下无意义-->
    <control mode="count" param="1000000" />
    <!--接力发送(发包模式)-->
    <!--本压测程序的核心,如何控制接力发送-->
    <!--mode: interval(固定间隔发包,连续发送,可调节interval), onsend(固定速度发包,接收到发送回调后接力投递下一次请求,可调节maxpps和ppsdelay)-->
    <!--mode: onrecv(接力发包,收到接收回调后接力投递下一次请求,可调节warmupcount和warmupinterval)-->
    <!--interval: loop模式下表示发包间隔,单位是毫秒-->
    <!--maxpps: onsend模式下表示最大发包速度-->
    <!--ppsdelay: onsend模式下,如果实时发包速度超过maxpps设置的值,那么会delay几毫秒再投递下一个包-->
    <!--warmupcount: onrecv模式下,需要预先投递的发送请求的个数-->
    <!--warmupinterval: onrecv模式下,投递发送请求时的间隔-->
    <relay mode="onrecv" interval="10" maxpps="1000" ppsdelay="5" warmupcount="50000" warmupinterval="10" />
    <!--Debug-->
    <!--sendpoolsize: 发包池大小-->
    <debug sendpoolsize="1000000" />
    <!--数据协议-->
    <!--本压测程序需要配合回显服务使用，如果发送和接收的数据需要添加自定包头，请指定此项-->
    <!--mode: raw(不需要指定包头，发送纯回显数据), fakes5(添加伪s5包头)-->
    <!--param1: 参数一-->
    <!--param2: 参数二-->
    <dataprotocol mode="raw" param1="invalid" param2="0" param3="0" param4="0" param5="0" />
    <!--Log打点-->
    <!--mode: count(按发包数量打点), time(按时间间隔打点)-->
    <!--param: 发包数量或者间隔时间(毫秒)-->
    <log mode="count" param="100000" />
</settings>
```
