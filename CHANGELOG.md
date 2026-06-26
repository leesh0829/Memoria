# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - 2026-06-26
### Added
- 일반 메모(plain) 에디터: 디바운스 자동 저장(약 500ms), 저장 버튼 없음.
- 체크리스트(일일 업무일지): 할 일(취소선)/이슈 항목, 고객사 자동 태깅 + 수동 교정.
- 주간보고 자동 생성: 양식 A(할일/이슈 순), 양식 B(고객사별 분류 + 제목줄), 클립보드 복사, 멱등 재생성.
- 고객사 자동 분류 규칙(우선순위 기반, 자율형공장 > SLD), 주차 계산(월~금).
- 그룹(분류) 트리, (미분류) 가상 노드, 시스템 그룹(일일업무일지/주간보고), 그룹 CRUD(ON DELETE SET NULL).
- 전문검색(FTS5): 제목 + 본문 + 체크리스트 항목.
- 휴지통(소프트 삭제) + 복원/영구삭제/Undo, 보존기간 자동 정리(기본 30일).
- 전역 단축키 `Ctrl+Alt+N`(message-only 창), 트레이 상주, 자동시작, 단일 인스턴스(named pipe).
- 테마: 라이트/다크/시스템 모드 + 강조색 + 프리셋 팔레트(모든 색 DynamicResource).
- 데이터 영속화: SQLite WAL, `%LOCALAPPDATA%\Memoria`, 크래시 복구 저널, 자동 백업(VACUUM INTO/Online Backup), 스키마 마이그레이션.
- 문서: README, 아키텍처, 주간보고 양식 규칙, 사용자 가이드(한글).
- CI/CD: GitHub Actions(build+test, 태그 릴리스 단일 exe 자산 첨부).

[Unreleased]: https://github.com/leesh0829/Memoria/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/leesh0829/Memoria/releases/tag/v0.1.0
