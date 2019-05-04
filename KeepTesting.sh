#!/bin/bash -v
sum=0
echo "* building"
dotnet build  --source EchoClientCore  
while ($true) 
do 
    echo "- doing guang zhou"
    dotnet run ConfigForGzTxCloud.xml --project EchoClientCore|grep "日志" >> LineDirectGZ.log
    echo "- doing hong kong"
    dotnet run ConfigDirectHongKong.xml --project EchoClientCore|grep "日志" >> LineDirectHK.log
    echo "- doing seoul"
    dotnet run ConfigDirectSeoul.xml --project EchoClientCore|grep "日志" >> LineDirectSeoul.log
    echo "- doing france"
    dotnet run ConfigDirectFrance.xml --project EchoClientCore|grep "日志" >> LineDirectFrance.log
    echo "- doing us sillicon"
    dotnet run ConfigDirectSilliconValley.xml --project EchoClientCore|grep "日志" >> LineDirectUS.log
    let "sum+=1"
    echo "done once, total: $sum times\r\n"
done