1. Tên đề tài:
   Ứng dụng bài toán Multi-Agent Pathfinding trong game bắn xe tăng nhiều người chơi
2. Lĩnh vực đề tài:

- Lựa chọn 1: Multimedia và Game
- Lựa chọn 2:
- Lựa chọn 3:
- Nếu lĩnh vực không nằm trong danh sách có sẵn, giáo viên hướng dẫn có thể đề xuất:

3. Mục tiêu của ĐATN:
   3.1. Kiến thức sinh viên thu thập được:
   "- Hiểu rõ bài toán Multi-Agent Pathfinding (MAPF) trong môi trường động và thời gian thực.

- Nắm được nguyên lý hoạt động của các thuật toán tìm đường như A\* search algorithm và các phương pháp nâng cao như Large Neighborhood Search 2.
- Hiểu được các thách thức trong điều hướng đa tác tử: va chạm, deadlock, tối ưu đường đi và phối hợp giữa nhiều agent.
- Nắm bắt các kỹ thuật chuyển đổi và tích hợp thuật toán từ môi trường C++ sang C# hoặc thông qua hệ thống server-client.
- Hiểu về thiết kế AI trong game realtime và các yếu tố ảnh hưởng đến độ khó và trải nghiệm người chơi."
  3.2. Công nghệ sinh viên thu thập được:
  "- Game Engine: Unity (C#) để phát triển môi trường game bắn xe tăng multiplayer.
- AI và Pathfinding: + A\* (baseline có sẵn trong Unity) + LNS2 (tham khảo từ team No Man's Sky – The League of Robot Runners 2024)
- System Integration: + Migration thuật toán từ C++ sang C# + Hoặc xây dựng hệ thống server để gọi thuật toán thông qua Start-kit v2.1.2
- Networking: Xây dựng hệ thống multiplayer (2–8 người chơi)
- Data Processing: Xử lý grid/map, biểu diễn môi trường và trạng thái agent
- Tools & Frameworks: + Visual Studio để Coding, Implement. + Git quản lý mã nguồn. + Unity NavMesh để custom grid system."
  3.3. Kỹ năng sinh viên phát triển được:
  "- Kỹ năng lập trình nâng cao (C#, hiểu và chuyển đổi từ C++).
- Kỹ năng thiết kế hệ thống AI đa tác tử theo hướng module hóa.
- Kỹ năng phân tích thuật toán và tối ưu hiệu năng trong môi trường realtime.
- Kỹ năng xây dựng game multiplayer và xử lý đồng bộ trạng thái.
- Kỹ năng nghiên cứu tài liệu kỹ thuật và mã nguồn mở.
- Kỹ năng thực nghiệm, đánh giá và so sánh các phương pháp khác nhau.
- Kỹ năng viết báo cáo kỹ thuật và trình bày kết quả."
  3.4. Sản phẩm kỳ vọng:
  "- Một game bắn xe tăng multiplayer (2–8 người chơi) có tích hợp hệ thống AI đa tác tử.
- Hệ thống AI gồm nhiều cấp độ: + Level 1: A\* (baseline) + Level 2: LNS2 + Level 3: LNS2 + heuristic cải tiến (Halpern)
- Module AI có khả năng: + Điều hướng nhiều agent đồng thời + Tránh va chạm và phối hợp tấn công
- Bộ kết quả thực nghiệm: + Định lượng: thời gian tìm đường, số va chạm, hiệu quả tấn công, tỉ lệ thắng + Định tính: video gameplay, hành vi AI giữa các cấp độ
- Tài liệu: + Báo cáo đồ án + Mã nguồn hệ thống (AI module + game demo)"
  3.5. Vấn đề thực tiễn đồ án giải quyết:
  "- Cải thiện chất lượng AI trong game multiplayer, tăng tính thử thách và trải nghiệm người chơi thông qua hành vi điều hướng thông minh và có phối hợp giữa nhiều agent.
- Giải quyết bài toán điều hướng đa tác tử (Multi-Agent Pathfinding) trong môi trường động, nơi nhiều tác nhân cùng hoạt động, tương tác và cạnh tranh tài nguyên.
- Ứng dụng trong lĩnh vực robotics và quản lý kho bãi thông minh, nơi các robot tự hành cần: + Tìm đường tối ưu trong không gian có nhiều chướng ngại vật. + Tránh va chạm với các robot khác. + Phối hợp để thực hiện nhiệm vụ như lấy hàng, di chuyển và sắp xếp hàng hóa.
- Góp phần xây dựng các hệ thống tự động hóa logistics, tương tự các mô hình kho hàng hiện đại (warehouse automation), nơi các robot cần hoạt động hiệu quả trong không gian giới hạn.
- Xây dựng giải pháp AI có thể tái sử dụng cho nhiều lĩnh vực: game, mô phỏng, robot tự hành và hệ thống đa tác tử."

4. Các nội dung sẽ thực hiện và kế hoạch triển khai:
   Lưu ý: khối lượng yêu cầu đối với đồ án tốt nghiệp hệ cử nhân là 6(0-0-12-12), i.e. 12 tiết làm việc/tuần trong 17 tuần.
   Nội dung 1: Tìm hiểu tổng quan về bài toán, từ Tuần 1 đến Tuần 4
   Chi tiết:
   "- Tổng quan về bài toán Multi-Agent Pathfinding (MAPF).

- Phân tích hạn chế của A\* trong môi trường đa tác tử.
- Tìm hiểu các vấn đề: va chạm, deadlock, scalability khi số lượng agent tăng.
- Khảo sát cơ chế AI trong game và các phương pháp điều hướng phổ biến.
- Xác định mục tiêu đề tài và phạm vi triển khai trong môi trường game tank multiplayer."

Nội dung 2: Tìm hiểu tổng quan về công nghệ liên quan, từ Tuần 2 đến Tuần 7
Chi tiết:
"- Nghiên cứu thuật toán LNS2 từ source code của team No Man’s Sky (Robot Runners 2024).

- Phân tích kiến trúc và cách triển khai LNS2 trong C++.
- Tìm hiểu Start-kit v2.1.2 và cơ chế tích hợp agent qua server.
- Nghiên cứu phương pháp migration từ C++ sang C#.
- Tìm hiểu hệ thống multiplayer trong Unity."

Nội dung 3: Phân tích thiết kế, từ Tuần 6 đến Tuần 10
Chi tiết:
"- Thiết kế môi trường game: map, agent, player, objective (“Đại bàng”).

- Thiết kế hệ thống AI đa cấp độ (A\*, LNS2, nâng cao).
- Xây dựng pipeline: + Input: trạng thái game + Process: tính toán đường đi + Output: hành động agent
- Thiết kế kiến trúc tích hợp: + Phương án 1: chạy local (C#) + Phương án 2: client-server (call LNS2 từ C++)
- Thiết kế các chỉ số đánh giá hiệu năng."

Nội dung 4: Xây dựng chương trình, từ Tuần 7 đến Tuần 15
Chi tiết:
"- Xây dựng game tank multiplayer cơ bản trong Unity.

- Triển khai A\* làm baseline cho agent.
- Tích hợp LNS2: + Migration sang C# hoặc + Kết nối server qua Start-kit
- Xây dựng hệ thống điều phối nhiều agent đồng thời.
- Phát triển logic AI (tấn công, phòng thủ, di chuyển).
- Triển khai LNS2 + Halpern heuristic."

Nội dung 5: Thử nghiệm và đánh giá, từ Tuần 14 đến Tuần 17
Chi tiết:
"- Xây dựng kịch bản test với 2–8 người chơi.

- So sánh các cấp độ AI: A\* vs LNS2 vs cải tiến.
- Đánh giá bằng các chỉ số: + Thời gian tìm đường. + Số va chạm. + Hiệu quả chiến đấu.
- Phân tích hành vi AI trong các tình huống thực tế.
- Đề xuất cải tiến và hướng phát triển mở rộng."
