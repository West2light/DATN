# R2 diagrams v2

Source set for thesis diagrams after the R2 refactor.

R2.1 update:

- Active draw.io diagrams now use a light palette with parent/child containers.
- `02_architecture_packages.drawio` was regenerated from scratch to avoid the previous draw.io load error.
- The class diagram moved to Mermaid as `mermaid/08_pathfinding_class_diagram.mmd`.
- The old draw.io class diagram source was removed to avoid export confusion.

## Structure

- `drawio/`: standalone `.drawio` files for system, architecture, activity, data-flow, algorithm path, backtest, and deployment diagrams.
- `mermaid/`: standalone `.mmd` files for sequence diagrams and the class diagram.
- `export/pdf/`: target folder for PDF exports.
- `export/png/`: target folder for PNG exports.

## Source files

### Draw.io

- `01_system_context.drawio`
- `02_architecture_packages.drawio`
- `03_single_play_activity.drawio`
- `04_internet_host_join_activity.drawio`
- `07_mapf_data_flow.drawio`
- `12_pibt_to_epibt_path.drawio`
- `13_backtest_workflow.drawio`
- `14_gcp_nginx_deployment.drawio`

### Mermaid

- `05_internet_create_room_sequence.mmd`
- `06_internet_ready_start_sequence.mmd`
- `08_pathfinding_class_diagram.mmd`
- `09_astar_replan_sequence.mmd`
- `10_pibt_csharp_sequence.mmd`
- `11_pibt_tcp_epibt_sequence.mmd`

## Chapter mapping

- Chapter 3:
  - `12_pibt_to_epibt_path.drawio`
- Chapter 4:
  - `01_system_context.drawio`
  - `02_architecture_packages.drawio`
  - `03_single_play_activity.drawio`
  - `04_internet_host_join_activity.drawio`
  - `05_internet_create_room_sequence.mmd`
  - `06_internet_ready_start_sequence.mmd`
  - `10_pibt_csharp_sequence.mmd`
  - `11_pibt_tcp_epibt_sequence.mmd`
  - `14_gcp_nginx_deployment.drawio`
- Chapter 5:
  - `07_mapf_data_flow.drawio`
  - `09_astar_replan_sequence.mmd`
  - `13_backtest_workflow.drawio`
- Appendix or optional:
  - `08_pathfinding_class_diagram.mmd`

## Export names

- `system_context.pdf`
- `architecture_packages.pdf`
- `single_play_activity_map.pdf`
- `internet_host_join_activity_map.pdf`
- `internet_create_room_sequence.pdf`
- `internet_ready_start_sequence.pdf`
- `mapf_data_flow.pdf`
- `pathfinding_class_diagram.pdf`
- `astar_replan_sequence.pdf`
- `pibt_csharp_sequence.pdf`
- `pibt_tcp_epibt_sequence.pdf`
- `pibt_to_epibt_path.pdf`
- `backtest_workflow.pdf`
- `gcp_nginx_deployment.pdf`

## Notes

- Activity diagrams use a swimlane activity-map layout with a white background, title row, lane header row, and vertical lane separators.
- Sequence diagrams stay in Mermaid for easier text edits and quick rendering.
- The class diagram also stays in Mermaid for easier maintenance and cleaner UML output.
- Long explanations, ports, API paths, and file references should stay in report captions or surrounding text, not inside the diagrams.
