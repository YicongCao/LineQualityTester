# coding=utf-8
import pandas as pd
import matplotlib.pyplot as plt

import os
import re


log_folder = "."


def TimeToFloat(dt):
    # dt_str: 2019-05-04 12:14:44
    # numbers: [('12', '14', '44')]
    dt_str = str(dt)
    # print("dt_str:", dt)
    pattern = re.compile(r'(\d+):(\d+):(\d+)')
    numbers = pattern.findall(dt_str)
    # print("numbers:", numbers)
    hour = float(numbers[0][0])
    minute = float(numbers[0][1])
    second = float(numbers[0][2])
    ft = 0.0
    ft += hour
    ft += minute / 60.0
    ft += second / 3600.0
    return ft


def LoadData(filename):
    file_path = os.path.join(log_folder, filename)
    data = pd.read_csv(file_path)
    data.columns = ['log_type', 'date_time', 'speed', 'latency',
                    'loss_percent', 'lost_count', 'sent_count', 'bandwidth']
    data['latency'] = data['latency'].map(
        lambda x: x.rstrip('ms')).astype(float)
    data['date_time'] = pd.to_datetime(data['date_time'])
    data['date_time_float'] = data['date_time'].map(
        lambda x: TimeToFloat(x)).astype(float)
    data['loss_percent'] = data['loss_percent'].map(
        lambda x: x.rstrip("%")).astype(float)
    # return data
    return pd.DataFrame(data)


def PlotOne(filename):
    data_gz = LoadData(filename + '.log')
    fig, ax = plt.subplots()
    plt.title(filename)
    ax2 = ax.twinx()
    p1, = ax.plot(data_gz['date_time_float'],
                  data_gz['latency'], 'r-', label='latency(ms)')
    p2, = ax2.plot(data_gz['date_time_float'],
                   data_gz['loss_percent'], 'b-', label='loss(%)')
    # ax.set_xlim(0, 24)
    ax.set_xlabel('hour(h)')
    ax.set_ylim(0, 600)
    ax.set_ylabel('latency(ms)')
    ax2.set_ylim(0, 100)
    ax2.set_ylabel('loss(%)')
    lines = [p1, p2]
    ax.legend(lines, [l.get_label() for l in lines])
    plt.xlabel('datetime')
    plt.savefig(filename + '.png')


if __name__ == "__main__":
    PlotOne('LineDirectGZ')
    PlotOne('LineDirectHK')
    PlotOne('LineDirectSeoul')
    PlotOne('LineDirectUS')
    PlotOne('LineDirectFrance')
    plt.show()
