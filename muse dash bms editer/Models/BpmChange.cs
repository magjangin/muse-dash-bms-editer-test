namespace bms_editer.Models;

// 곡 도중의 BPM 변화 한 건.
//
// BMS 는 두 가지 방법으로 BPM 을 바꾼다.
//   #xxx03 — 16진수 두 자리를 그대로 BPM 으로 쓴다. 정수만 되고 255가 한계다.
//   #xxx08 — base-36 번호로 #BPMxx 표를 가리킨다. 소수점도 되고 255도 넘는다.
// 어느 쪽으로 왔든 여기서는 "몇 마디 몇 번째 지점에서 BPM 이 얼마로 바뀐다"로만 다룬다.
public readonly record struct BpmChange(int Measure, double Position, double Bpm)
{
    // 곡 처음부터 센 위치. 마디 1의 한가운데면 1.5.
    public double MeasurePosition => Measure + Position;
}
